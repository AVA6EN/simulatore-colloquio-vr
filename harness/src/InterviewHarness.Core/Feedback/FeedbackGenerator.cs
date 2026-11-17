using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using InterviewHarness.Config;
using InterviewHarness.Exchange;
using InterviewHarness.Llm;
using InterviewHarness.Prompting;
using InterviewHarness.Session;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace InterviewHarness.Feedback
{
    /// <summary>
    /// Genera il feedback finale secondo la griglia in config/evaluation.yaml.
    /// Il modello assegna punteggio, motivazione ed evidenze per criterio;
    /// il voto complessivo è calcolato qui (media pesata), in modo deterministico.
    /// </summary>
    public class FeedbackGenerator
    {
        private const int MaxAttempts = 2;

        private readonly HarnessConfig _config;
        private readonly ILlmClient _llm;
        private readonly PromptBuilder _prompts;

        public FeedbackGenerator(HarnessConfig config, ILlmClient llm, PromptBuilder prompts)
        {
            _config = config;
            _llm = llm;
            _prompts = prompts;
        }

        public async Task<FeedbackReport> GenerateAsync(SessionSetup setup, IReadOnlyList<TranscriptEntry> transcript, CancellationToken ct = default)
        {
            var grid = _config.Evaluation;
            string system = _prompts.BuildFeedbackSystem(setup, FormatTranscript(setup, transcript));
            string schema = OutputSchemas.Feedback(grid.Criteria.Select(c => c.Id));
            string lastError = "";

            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                var response = await _llm.CompleteAsync(new LlmRequest
                {
                    Purpose = "feedback",
                    Model = _config.Settings.Model,
                    System = system,
                    Messages = new List<LlmMessage> { new LlmMessage(LlmRole.User, "Scrivi ora il feedback del colloquio.") },
                    MaxTokens = _config.Settings.FeedbackMaxTokens,
                    Effort = _config.Settings.FeedbackEffort,
                    JsonSchema = schema,
                }, ct).ConfigureAwait(false);

                var report = TryBuildReport(response.Text, grid, out lastError);
                if (report != null) return report;
            }
            throw new HarnessException("Impossibile generare il feedback: " + lastError);
        }

        public static string FormatTranscript(SessionSetup setup, IReadOnlyList<TranscriptEntry> transcript)
        {
            var sb = new StringBuilder();
            foreach (var e in transcript)
            {
                string who = e.Speaker == "recruiter" ? $"Recruiter ({setup.Persona.Name})" : $"Candidato ({setup.Student.FirstName})";
                sb.AppendLine($"[{e.Phase}] {who}: {(string.IsNullOrEmpty(e.Text) ? "(nessuna risposta)" : e.Text)}");
            }
            return sb.ToString();
        }

        /// <summary>Costruisce il report dal JSON del modello; null se mancano dati.</summary>
        public static FeedbackReport? TryBuildReport(string raw, EvaluationGrid grid, out string error)
        {
            error = "";
            int start = raw?.IndexOf('{') ?? -1;
            int end = raw?.LastIndexOf('}') ?? -1;
            if (raw == null || start < 0 || end <= start) { error = "risposta non JSON"; return null; }

            JObject obj;
            try { obj = JObject.Parse(raw.Substring(start, end - start + 1)); }
            catch (JsonException ex) { error = "JSON non valido: " + ex.Message; return null; }

            var items = (obj["criteria"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
            var report = new FeedbackReport
            {
                Summary = (string?)obj["summary"] ?? "",
                Strengths = ToStringList(obj["strengths"]),
                Improvements = ToStringList(obj["improvements"]),
            };

            foreach (var criterion in grid.Criteria)
            {
                var item = items.FirstOrDefault(i => (string?)i["id"] == criterion.Id);
                if (item == null) { error = $"manca il criterio '{criterion.Id}'"; return null; }

                int score = item["score"]?.Type == JTokenType.Integer ? (int)item["score"]! : 0;
                report.Criteria.Add(new CriterionFeedback
                {
                    Id = criterion.Id,
                    Name = criterion.Name,
                    Weight = criterion.Weight,
                    Score = Math.Max(1, Math.Min(5, score)),
                    Motivation = (string?)item["motivation"] ?? "",
                    Evidence = ToStringList(item["evidence"]),
                    Suggestion = (string?)item["suggestion"] ?? "",
                });
            }

            report.OverallScore = OverallScore(report.Criteria);
            report.OverallLabel = Label(grid, report.OverallScore);
            return report;
        }

        public static double OverallScore(IReadOnlyCollection<CriterionFeedback> criteria)
        {
            double totalWeight = criteria.Sum(c => c.Weight);
            if (totalWeight <= 0) return 0;
            return Math.Round(criteria.Sum(c => c.Score * c.Weight) / totalWeight, 1, MidpointRounding.AwayFromZero);
        }

        public static string Label(EvaluationGrid grid, double score)
        {
            int rounded = (int)Math.Round(score, MidpointRounding.AwayFromZero);
            return grid.Scale.TryGetValue(rounded, out var label) ? label : "";
        }

        private static List<string> ToStringList(JToken? token) =>
            token is JArray arr ? arr.Select(t => t.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList() : new List<string>();
    }
}
