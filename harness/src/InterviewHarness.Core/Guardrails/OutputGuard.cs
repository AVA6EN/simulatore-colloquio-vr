using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using InterviewHarness.Config;
using InterviewHarness.Exchange;

namespace InterviewHarness.Guardrails
{
    public class GuardViolation
    {
        public string Code { get; }
        public string Message { get; }
        public GuardViolation(string code, string message) { Code = code; Message = message; }
        public override string ToString() => $"{Code}: {Message}";
    }

    /// <summary>Output grezzo del modello per una battuta del recruiter (prima dei controlli).</summary>
    public class RecruiterModelOutput
    {
        public string Speech { get; set; } = "";
        public string Emotion { get; set; } = "neutral";
        public string Gesture { get; set; } = "none";
        public double Intensity { get; set; } = 0.5;
        public bool PhaseGoalMet { get; set; }
        public List<string> WeakPoints { get; set; } = new List<string>();
    }

    /// <summary>
    /// Controlli automatici sulla battuta del recruiter: lunghezza, numero di domande,
    /// permanenza nel ruolo, domande vietate, formattazione non pronunciabile.
    /// </summary>
    public class OutputGuard
    {
        private readonly GuardrailsConfig _config;
        private static readonly Regex Markdown = new Regex(@"(\*\*|__|^\s*[-*#>]\s|`|\[[^\]]*\]\()", RegexOptions.Multiline | RegexOptions.Compiled);

        public OutputGuard(GuardrailsConfig config) { _config = config; }

        public List<GuardViolation> Check(RecruiterModelOutput output)
        {
            var violations = new List<GuardViolation>();
            string speech = output.Speech ?? "";

            if (string.IsNullOrWhiteSpace(speech))
            {
                violations.Add(new GuardViolation("empty_speech", "La battuta è vuota."));
                return violations;
            }

            int words = CountWords(speech);
            if (words > _config.MaxSpeechWords)
                violations.Add(new GuardViolation("too_long",
                    $"La battuta ha {words} parole, il massimo è {_config.MaxSpeechWords}. Accorciala."));

            int questions = speech.Count(c => c == '?');
            if (questions > _config.MaxQuestionsPerTurn)
                violations.Add(new GuardViolation("too_many_questions",
                    $"La battuta contiene {questions} domande, ne è ammessa al massimo {_config.MaxQuestionsPerTurn}."));

            string normalized = Normalize(speech);

            foreach (var phrase in _config.RoleBreakPhrases)
                if (normalized.Contains(Normalize(phrase)))
                {
                    violations.Add(new GuardViolation("role_break",
                        $"La battuta esce dal ruolo di recruiter (\"{phrase}\"). Resta sempre nel personaggio."));
                    break;
                }

            foreach (var topic in _config.ForbiddenTopics)
                if (topic.Patterns.Any(p => normalized.Contains(Normalize(p))))
                    violations.Add(new GuardViolation("forbidden_topic:" + topic.Id,
                        $"La battuta tocca un argomento vietato nei colloqui ({topic.Id}). Riformulala senza questo argomento."));

            if (Markdown.IsMatch(speech))
                violations.Add(new GuardViolation("markdown",
                    "La battuta contiene formattazione (elenchi, grassetto, link) che non si può pronunciare. Scrivi testo semplice."));

            return violations;
        }

        /// <summary>
        /// Corregge i campi che non richiedono di rigenerare la battuta
        /// (emozione/gesto fuori vocabolario, intensità fuori intervallo).
        /// </summary>
        public static void Sanitize(RecruiterModelOutput output, string defaultEmotion)
        {
            if (!ExchangeVocabulary.Emotions.Contains(output.Emotion)) output.Emotion = defaultEmotion;
            if (!ExchangeVocabulary.Gestures.Contains(output.Gesture)) output.Gesture = "none";
            if (double.IsNaN(output.Intensity)) output.Intensity = 0.5;
            output.Intensity = Math.Max(0.0, Math.Min(1.0, output.Intensity));
            output.Speech = (output.Speech ?? "").Trim();
            output.WeakPoints = (output.WeakPoints ?? new List<string>())
                .Where(w => !string.IsNullOrWhiteSpace(w)).Select(w => w.Trim()).ToList();
        }

        public static int CountWords(string text) =>
            text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;

        private static string Normalize(string text) =>
            text.ToLowerInvariant().Replace('’', '\'').Replace('`', '\'');
    }
}
