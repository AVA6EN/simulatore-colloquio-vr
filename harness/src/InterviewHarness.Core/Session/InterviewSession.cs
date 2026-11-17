using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InterviewHarness.Config;
using InterviewHarness.Exchange;
using InterviewHarness.Feedback;
using InterviewHarness.Guardrails;
using InterviewHarness.Llm;
using InterviewHarness.Prompting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace InterviewHarness.Session
{
    public class TranscriptEntry
    {
        public string Speaker { get; set; } = "";   // "recruiter" | "candidate"
        public string Phase { get; set; } = "";
        public string Text { get; set; } = "";
    }

    /// <summary>Cosa è successo in un turno del recruiter: utile per log, test e valutazioni.</summary>
    public class TurnRecord
    {
        public RecruiterTurn Turn { get; set; } = new RecruiterTurn();
        public List<string> Violations { get; set; } = new List<string>();
        public int Retries { get; set; }
        public bool UsedFallback { get; set; }
        public long InputTokens { get; set; }
        public long OutputTokens { get; set; }
    }

    /// <summary>
    /// Un colloquio. Gestisce le fasi (macchina a stati), il contesto inviato al modello,
    /// i controlli sull'output e produce i messaggi del formato di scambio per Unity.
    /// </summary>
    public class InterviewSession
    {
        public const string ClosingFallbackSpeech =
            "Grazie per il tuo tempo, il colloquio è terminato. A breve riceverai un riscontro scritto. Arrivederci!";

        private const int MaxWeakPoints = 8;

        private readonly HarnessConfig _config;
        private readonly ILlmClient _llm;
        private readonly PromptBuilder _prompts;
        private readonly OutputGuard _guard;
        private readonly PiiRedactor _redactor;
        private readonly string _redactedProfile;

        private readonly List<LlmMessage> _history = new List<LlmMessage>();
        private readonly List<TranscriptEntry> _transcript = new List<TranscriptEntry>();
        private readonly List<TurnRecord> _turns = new List<TurnRecord>();
        private readonly List<string> _weakPoints = new List<string>();

        private int _phaseIndex;
        private int _questionsInPhase;
        private bool _lastGoalMet;
        private bool _started;

        public SessionSetup Setup { get; }
        public bool IsFinished { get; private set; }
        public string CurrentPhaseId => _config.Phases[_phaseIndex].Id;
        public IReadOnlyList<TranscriptEntry> Transcript => _transcript;
        public IReadOnlyList<TurnRecord> Turns => _turns;
        public IReadOnlyList<string> WeakPoints => _weakPoints;
        /// <summary>Ultimo system prompt inviato (per debug e test).</summary>
        public string LastSystemPrompt { get; private set; } = "";

        public InterviewSession(HarnessConfig config, ILlmClient llm, SessionSetup setup)
        {
            _config = config;
            _llm = llm;
            Setup = setup;
            _prompts = new PromptBuilder(config);
            _guard = new OutputGuard(config.Guardrails);
            _redactor = new PiiRedactor(config.Guardrails.PiiRedaction);
            _redactedProfile = _redactor.Redact($"Corso: {setup.Student.Course}\n\n{setup.Student.Cv}");
        }

        /// <summary>Prima battuta del recruiter (accoglienza).</summary>
        public Task<RecruiterTurn> StartAsync(CancellationToken ct = default)
        {
            if (_started) throw new HarnessException("Il colloquio è già iniziato.");
            _started = true;
            _history.Add(new LlmMessage(LlmRole.User,
                $"[Il candidato {Setup.Student.FirstName} entra nella stanza e si siede davanti a te. Inizia il colloquio.]"));
            return GenerateTurnAsync(ct);
        }

        /// <summary>Riceve la risposta dello studente e restituisce la battuta successiva del recruiter.</summary>
        public Task<RecruiterTurn> RespondAsync(string candidateText, CancellationToken ct = default)
        {
            if (!_started) throw new HarnessException("Chiama StartAsync prima di RespondAsync.");
            if (IsFinished) throw new HarnessException("Il colloquio è già terminato.");

            string text = _redactor.Redact((candidateText ?? "").Trim());
            _transcript.Add(new TranscriptEntry { Speaker = "candidate", Phase = CurrentPhaseId, Text = text });
            _history.Add(new LlmMessage(LlmRole.User,
                string.IsNullOrEmpty(text) ? "[Il candidato resta in silenzio e non risponde.]" : text));

            AdvancePhaseIfNeeded();
            return GenerateTurnAsync(ct);
        }

        public Task<RecruiterTurn> RespondAsync(CandidateTurn message, CancellationToken ct = default) =>
            RespondAsync(message.Text, ct);

        /// <summary>Feedback finale secondo la griglia di valutazione.</summary>
        public Task<FeedbackReport> GenerateFeedbackAsync(CancellationToken ct = default) =>
            new FeedbackGenerator(_config, _llm, _prompts).GenerateAsync(Setup, _transcript, ct);

        public async Task<InterviewEnd> EndAsync(CancellationToken ct = default) =>
            new InterviewEnd { Feedback = await GenerateFeedbackAsync(ct).ConfigureAwait(false) };

        // ------------------------------------------------------------------

        private (int min, int max) CurrentLimits() =>
            PromptBuilder.PhaseLimits(_config.Phases[_phaseIndex], Setup.Difficulty);

        private bool IsLastPhase => _phaseIndex == _config.Phases.Count - 1;

        /// <summary>
        /// La harness decide il passaggio di fase: si avanza se il modello ha segnalato l'obiettivo
        /// raggiunto (e sono state fatte abbastanza domande) oppure se si è al massimo di domande.
        /// </summary>
        private void AdvancePhaseIfNeeded()
        {
            if (IsLastPhase) return;
            var (min, max) = CurrentLimits();
            bool goalReached = _lastGoalMet && _questionsInPhase >= min;
            if (goalReached || _questionsInPhase >= max)
            {
                _phaseIndex++;
                _questionsInPhase = 0;
                _lastGoalMet = false;
            }
        }

        private async Task<RecruiterTurn> GenerateTurnAsync(CancellationToken ct)
        {
            var phase = _config.Phases[_phaseIndex];
            var (min, max) = CurrentLimits();
            bool forcedFinal = IsLastPhase && _questionsInPhase >= max - 1;

            string system = _prompts.BuildRecruiterSystem(Setup, _redactedProfile,
                new PhaseState { PhaseIndex = _phaseIndex, QuestionsAsked = _questionsInPhase, MinQuestions = min, MaxQuestions = max },
                _weakPoints);
            if (forcedFinal)
                system += "\n\n# Nota\n\nQuesta è la tua ULTIMA battuta: ringrazia il candidato, spiega che riceverà un riscontro scritto " +
                          "e congedalo. Non fare domande. Imposta `phase_goal_met` a true.";
            LastSystemPrompt = system;

            var record = new TurnRecord();
            RecruiterModelOutput? accepted = null;
            string? lastRaw = null;
            List<GuardViolation> violations = new List<GuardViolation>();

            for (int attempt = 0; attempt <= _config.Settings.MaxGuardRetries; attempt++)
            {
                var messages = new List<LlmMessage>(_history);
                if (attempt > 0 && lastRaw != null)
                {
                    messages.Add(new LlmMessage(LlmRole.Assistant, lastRaw));
                    messages.Add(new LlmMessage(LlmRole.User,
                        "[Nota per il recruiter, non detta dal candidato: la battuta precedente non rispetta le regole. " +
                        string.Join(" ", violations.Select(v => v.Message)) +
                        " Riscrivi la battuta rispondendo all'ultimo intervento del candidato.]"));
                }

                var response = await _llm.CompleteAsync(new LlmRequest
                {
                    Purpose = "recruiter_turn",
                    Model = _config.Settings.Model,
                    System = system,
                    Messages = messages,
                    MaxTokens = _config.Settings.TurnMaxTokens,
                    Effort = _config.Settings.TurnEffort,
                    JsonSchema = OutputSchemas.RecruiterTurn(ExchangeVocabulary.Emotions, ExchangeVocabulary.Gestures),
                }, ct).ConfigureAwait(false);

                record.InputTokens += response.InputTokens;
                record.OutputTokens += response.OutputTokens;
                lastRaw = response.Text;

                var parsed = TryParse(response.Text);
                if (parsed == null)
                {
                    violations = new List<GuardViolation> { new GuardViolation("invalid_json", "La risposta non è un oggetto JSON valido nel formato richiesto.") };
                }
                else
                {
                    OutputGuard.Sanitize(parsed, phase.DefaultEmotion);
                    violations = _guard.Check(parsed);
                }

                record.Violations.AddRange(violations.Select(v => v.Code));
                if (violations.Count == 0) { accepted = parsed; break; }
                if (attempt < _config.Settings.MaxGuardRetries) record.Retries++;
            }

            if (accepted == null)
            {
                record.UsedFallback = true;
                accepted = new RecruiterModelOutput
                {
                    Speech = forcedFinal ? ClosingFallbackSpeech : _config.Guardrails.FallbackSpeech,
                    Emotion = forcedFinal ? "friendly" : "neutral",
                    Gesture = forcedFinal ? "wave" : "nod",
                    Intensity = 0.4,
                    PhaseGoalMet = forcedFinal,
                };
            }

            _questionsInPhase++;
            _lastGoalMet = accepted.PhaseGoalMet;
            MergeWeakPoints(accepted.WeakPoints);

            // Nell'ultima fase il colloquio finisce quando il recruiter si congeda
            // (obiettivo raggiunto e nessuna domanda nella battuta) o al massimo di battute.
            bool end = IsLastPhase && (forcedFinal || (accepted.PhaseGoalMet && _questionsInPhase >= min && !accepted.Speech.Contains("?")));

            var turn = new RecruiterTurn
            {
                TurnId = _turns.Count + 1,
                Phase = phase.Id,
                Speech = accepted.Speech,
                Emotion = accepted.Emotion,
                Gesture = accepted.Gesture,
                Intensity = accepted.Intensity,
                EndOfInterview = end,
            };
            record.Turn = turn;
            _turns.Add(record);
            IsFinished = end;

            _history.Add(new LlmMessage(LlmRole.Assistant, Serialize(accepted)));
            _transcript.Add(new TranscriptEntry { Speaker = "recruiter", Phase = phase.Id, Text = accepted.Speech });
            return turn;
        }

        private void MergeWeakPoints(IEnumerable<string> newPoints)
        {
            foreach (var p in newPoints)
            {
                if (_weakPoints.Any(w => string.Equals(w, p, StringComparison.OrdinalIgnoreCase))) continue;
                _weakPoints.Add(_redactor.Redact(p));
            }
            while (_weakPoints.Count > MaxWeakPoints) _weakPoints.RemoveAt(0);
        }

        /// <summary>Legge il JSON del modello; tollera eventuali ```json ... ``` attorno.</summary>
        public static RecruiterModelOutput? TryParse(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            int start = raw.IndexOf('{');
            int end = raw.LastIndexOf('}');
            if (start < 0 || end <= start) return null;
            try
            {
                var obj = JObject.Parse(raw.Substring(start, end - start + 1));
                return new RecruiterModelOutput
                {
                    Speech = (string?)obj["speech"] ?? "",
                    Emotion = (string?)obj["emotion"] ?? "neutral",
                    Gesture = (string?)obj["gesture"] ?? "none",
                    Intensity = obj["intensity"]?.Type == JTokenType.Float || obj["intensity"]?.Type == JTokenType.Integer
                        ? (double)obj["intensity"]! : 0.5,
                    PhaseGoalMet = obj["phase_goal_met"]?.Type == JTokenType.Boolean && (bool)obj["phase_goal_met"]!,
                    WeakPoints = obj["weak_points"] is JArray arr
                        ? arr.Select(t => t.ToString()).ToList()
                        : new List<string>(),
                };
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string Serialize(RecruiterModelOutput o) => new JObject
        {
            ["speech"] = o.Speech,
            ["emotion"] = o.Emotion,
            ["gesture"] = o.Gesture,
            ["intensity"] = o.Intensity,
            ["phase_goal_met"] = o.PhaseGoalMet,
            ["weak_points"] = new JArray(o.WeakPoints.Cast<object>().ToArray()),
        }.ToString(Formatting.None);
    }
}
