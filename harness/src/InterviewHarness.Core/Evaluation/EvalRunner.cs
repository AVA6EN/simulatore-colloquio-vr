using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InterviewHarness.Config;
using InterviewHarness.Exchange;
using InterviewHarness.Llm;
using InterviewHarness.Session;

namespace InterviewHarness.Evaluation
{
    public class EvalCheck
    {
        public string Name { get; set; } = "";
        public bool Passed { get; set; }
        public string Detail { get; set; } = "";
    }

    public class EvalResult
    {
        public EvalCase Case { get; set; } = new EvalCase();
        public List<EvalCheck> Checks { get; set; } = new List<EvalCheck>();
        public IReadOnlyList<TranscriptEntry> Transcript { get; set; } = new List<TranscriptEntry>();
        public IReadOnlyList<TurnRecord> Turns { get; set; } = new List<TurnRecord>();
        public FeedbackReport? Feedback { get; set; }
        public string? Error { get; set; }
        public long InputTokens { get; set; }
        public long OutputTokens { get; set; }
        public bool Passed => Error == null && Checks.All(c => c.Passed);
    }

    /// <summary>
    /// Esegue un caso di test: simula un colloquio completo, genera il feedback e
    /// verifica le aspettative. Serve a controllare in modo ripetibile che una modifica
    /// alla harness non peggiori il comportamento del recruiter.
    /// </summary>
    public class EvalRunner
    {
        private readonly HarnessConfig _config;
        private readonly ILlmClient _llm;

        public EvalRunner(HarnessConfig config, ILlmClient llm)
        {
            _config = config;
            _llm = llm;
        }

        public async Task<EvalResult> RunAsync(EvalCase evalCase, CancellationToken ct = default)
        {
            var result = new EvalResult { Case = evalCase };
            InterviewSession? session = null;
            try
            {
                var setup = SessionSetup.FromIds(_config, evalCase.Setup.Student, evalCase.Setup.Position,
                    evalCase.Setup.Recruiter, evalCase.Setup.Difficulty);
                session = new InterviewSession(_config, _llm, setup);
                var candidate = new CandidateSimulator(_config, _llm, evalCase.Candidate);

                var turn = await session.StartAsync(ct).ConfigureAwait(false);
                int turns = 1;
                while (!turn.EndOfInterview && turns < evalCase.MaxTurns)
                {
                    string answer = await candidate.AnswerAsync(session.Transcript, ct).ConfigureAwait(false);
                    turn = await session.RespondAsync(answer, ct).ConfigureAwait(false);
                    turns++;
                }

                if (session.IsFinished)
                    result.Feedback = await session.GenerateFeedbackAsync(ct).ConfigureAwait(false);

                result.InputTokens = candidate.InputTokens;
                result.OutputTokens = candidate.OutputTokens;
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                result.Error = ex.Message;
            }

            if (session != null)
            {
                result.Transcript = session.Transcript;
                result.Turns = session.Turns;
                result.InputTokens += session.Turns.Sum(t => t.InputTokens);
                result.OutputTokens += session.Turns.Sum(t => t.OutputTokens);
                result.Checks = Evaluate(evalCase, session, result.Feedback);
            }
            return result;
        }

        public List<EvalCheck> Evaluate(EvalCase evalCase, InterviewSession session, FeedbackReport? feedback)
        {
            var expect = evalCase.Expect;
            var checks = new List<EvalCheck>();
            var recruiterText = string.Join("\n", session.Transcript.Where(t => t.Speaker == "recruiter").Select(t => t.Text));
            string lower = recruiterText.ToLowerInvariant();

            if (expect.Completes)
                checks.Add(Check("completa il colloquio", session.IsFinished,
                    session.IsFinished ? $"{session.Turns.Count} battute" : $"non concluso dopo {session.Turns.Count} battute"));

            if (expect.AllPhases)
            {
                var visited = session.Turns.Select(t => t.Turn.Phase).Distinct().ToList();
                var missing = _config.Phases.Select(p => p.Id).Except(visited).ToList();
                checks.Add(Check("attraversa tutte le fasi", missing.Count == 0,
                    missing.Count == 0 ? string.Join(" → ", visited) : "mancano: " + string.Join(", ", missing)));
            }

            int fallbacks = session.Turns.Count(t => t.UsedFallback);
            checks.Add(Check($"battute di riserva ≤ {expect.MaxFallbacks}", fallbacks <= expect.MaxFallbacks, fallbacks.ToString()));

            if (expect.MaxGuardViolations.HasValue)
            {
                int blocked = session.Turns.Sum(t => t.Violations.Count);
                checks.Add(Check($"violazioni intercettate ≤ {expect.MaxGuardViolations}", blocked <= expect.MaxGuardViolations,
                    blocked == 0 ? "0" : $"{blocked}: " + string.Join(", ", session.Turns.SelectMany(t => t.Violations).Distinct())));
            }

            if (expect.RecruiterMentionsAny.Count > 0)
            {
                var found = expect.RecruiterMentionsAny.Where(w => lower.Contains(w.ToLowerInvariant())).ToList();
                checks.Add(Check("personalizza le domande sul CV", found.Count > 0,
                    found.Count > 0 ? "cita: " + string.Join(", ", found) : "nessun riferimento a: " + string.Join(", ", expect.RecruiterMentionsAny)));
            }

            if (expect.RecruiterNeverSays.Count > 0)
            {
                var found = expect.RecruiterNeverSays.Where(w => lower.Contains(w.ToLowerInvariant())).ToList();
                checks.Add(Check("non dice frasi vietate", found.Count == 0, found.Count == 0 ? "ok" : "dice: " + string.Join(", ", found)));
            }

            if (expect.OverallScore != null || expect.Criteria.Count > 0)
            {
                if (feedback == null)
                {
                    checks.Add(Check("feedback generato", false, "nessun feedback"));
                }
                else
                {
                    if (expect.OverallScore != null)
                        checks.Add(RangeCheck("voto complessivo", feedback.OverallScore, expect.OverallScore));
                    foreach (var kv in expect.Criteria)
                    {
                        var c = feedback.Criteria.FirstOrDefault(x => x.Id == kv.Key);
                        checks.Add(c == null
                            ? Check($"criterio {kv.Key}", false, "criterio assente")
                            : RangeCheck($"criterio {kv.Key}", c.Score, kv.Value));
                    }
                    bool evidenceOk = feedback.Criteria.Where(c => c.Score >= 3).All(c => c.Evidence.Count > 0);
                    checks.Add(Check("i punteggi ≥ 3 hanno evidenze citate", evidenceOk,
                        evidenceOk ? "ok" : string.Join(", ", feedback.Criteria.Where(c => c.Score >= 3 && c.Evidence.Count == 0).Select(c => c.Id))));
                }
            }
            return checks;
        }

        private static EvalCheck Check(string name, bool passed, string detail) =>
            new EvalCheck { Name = name, Passed = passed, Detail = detail };

        private static EvalCheck RangeCheck(string name, double value, ScoreRange range)
        {
            bool ok = (!range.Min.HasValue || value >= range.Min.Value) && (!range.Max.HasValue || value <= range.Max.Value);
            string bounds = $"[{range.Min?.ToString(CultureInfo.InvariantCulture) ?? "-"}, {range.Max?.ToString(CultureInfo.InvariantCulture) ?? "-"}]";
            return Check($"{name} in {bounds}", ok, value.ToString("0.#", CultureInfo.InvariantCulture));
        }
    }
}
