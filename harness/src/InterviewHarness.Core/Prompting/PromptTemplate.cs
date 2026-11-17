using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace InterviewHarness.Prompting
{
    /// <summary>Template molto semplice: sostituisce i segnaposto {{nome}}.</summary>
    public static class PromptTemplate
    {
        private static readonly Regex Placeholder = new Regex(@"\{\{\s*([a-zA-Z0-9_]+)\s*\}\}", RegexOptions.Compiled);

        public static string Render(string template, IReadOnlyDictionary<string, string> values)
        {
            var missing = FindPlaceholders(template).Where(p => !values.ContainsKey(p)).ToList();
            if (missing.Count > 0)
                throw new HarnessException("Segnaposto senza valore nel prompt: " + string.Join(", ", missing));
            return Placeholder.Replace(template, m => values[m.Groups[1].Value]);
        }

        public static IReadOnlyCollection<string> FindPlaceholders(string template) =>
            Placeholder.Matches(template).Cast<Match>().Select(m => m.Groups[1].Value).Distinct().ToList();
    }
}
