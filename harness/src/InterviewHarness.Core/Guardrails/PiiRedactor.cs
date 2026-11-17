using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using InterviewHarness.Config;

namespace InterviewHarness.Guardrails
{
    /// <summary>
    /// Oscura i dati personali (email, telefono, codice fiscale, IBAN...) prima che
    /// CV e risposte dello studente vengano inviati al modello.
    /// </summary>
    public class PiiRedactor
    {
        private readonly List<(Regex regex, string replacement)> _rules;

        public PiiRedactor(IEnumerable<PiiRule> rules)
        {
            _rules = rules.Select(r => (new Regex(r.Pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant), r.Replacement)).ToList();
        }

        public string Redact(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            foreach (var (regex, replacement) in _rules)
                text = regex.Replace(text, replacement);
            return text;
        }
    }
}
