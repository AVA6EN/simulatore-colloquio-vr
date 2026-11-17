using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using InterviewHarness.Config;
using InterviewHarness.Exchange;
using InterviewHarness.Session;

namespace InterviewHarness.Prompting
{
    /// <summary>Stato della fase corrente che finisce nel prompt del recruiter.</summary>
    public class PhaseState
    {
        public int PhaseIndex { get; set; }
        public int QuestionsAsked { get; set; }
        public int MinQuestions { get; set; }
        public int MaxQuestions { get; set; }
    }

    /// <summary>
    /// Compone i prompt a partire dai file in harness/prompts/ e dalla configurazione.
    /// Il system prompt viene ricostruito a ogni turno: cambia la fase, cambiano le istruzioni.
    /// </summary>
    public class PromptBuilder
    {
        private readonly HarnessConfig _config;
        private readonly string _recruiterTemplate;
        private readonly string _feedbackTemplate;
        private readonly Dictionary<string, string> _phaseInstructions;

        public PromptBuilder(HarnessConfig config)
        {
            _config = config;
            _recruiterTemplate = config.ReadPrompt("system/recruiter.md");
            _feedbackTemplate = config.ReadPrompt("feedback.md");
            _phaseInstructions = config.Phases.ToDictionary(p => p.Id, p => config.ReadPrompt("phases/" + p.Prompt));
        }

        public string BuildRecruiterSystem(SessionSetup setup, string redactedProfile, PhaseState state, IReadOnlyList<string> weakPoints)
        {
            var phase = _config.Phases[state.PhaseIndex];
            var values = new Dictionary<string, string>
            {
                ["recruiter_name"] = setup.Persona.Name,
                ["recruiter_role"] = setup.Persona.Role,
                ["recruiter_style"] = setup.Persona.Style.Trim(),
                ["recruiter_focus"] = string.Join(", ", setup.Persona.Focus),
                ["difficulty_name"] = setup.Difficulty.Name,
                ["difficulty_behavior"] = setup.Difficulty.Behavior.Trim(),
                ["position_title"] = setup.Position.Title,
                ["company_name"] = setup.Position.Company,
                ["position_description"] = setup.Position.Description.Trim(),
                ["student_first_name"] = setup.Student.FirstName,
                ["student_profile"] = redactedProfile.Trim(),
                ["weak_points"] = weakPoints.Count == 0 ? "Nessuno finora." : string.Join("\n", weakPoints.Select(w => "- " + w)),
                ["max_speech_words"] = _config.Guardrails.MaxSpeechWords.ToString(CultureInfo.InvariantCulture),
                ["phase_name"] = phase.Name,
                ["phase_number"] = (state.PhaseIndex + 1).ToString(CultureInfo.InvariantCulture),
                ["phase_count"] = _config.Phases.Count.ToString(CultureInfo.InvariantCulture),
                ["phase_goal"] = phase.Goal,
                ["phase_questions_asked"] = state.QuestionsAsked.ToString(CultureInfo.InvariantCulture),
                ["phase_min_questions"] = state.MinQuestions.ToString(CultureInfo.InvariantCulture),
                ["phase_max_questions"] = state.MaxQuestions.ToString(CultureInfo.InvariantCulture),
                ["phase_instructions"] = _phaseInstructions[phase.Id].Trim(),
                ["allowed_emotions"] = string.Join(", ", ExchangeVocabulary.Emotions),
                ["allowed_gestures"] = string.Join(", ", ExchangeVocabulary.Gestures),
            };
            return PromptTemplate.Render(_recruiterTemplate, values);
        }

        public string BuildFeedbackSystem(SessionSetup setup, string transcript)
        {
            var grid = _config.Evaluation;
            var scale = string.Join("\n", grid.Scale.OrderBy(kv => kv.Key).Select(kv => $"- {kv.Key} = {kv.Value}"));

            var criteria = new StringBuilder();
            foreach (var c in grid.Criteria)
            {
                criteria.AppendLine($"### `{c.Id}` — {c.Name} (peso {c.Weight.ToString("0.##", CultureInfo.InvariantCulture)})");
                criteria.AppendLine(c.Description);
                foreach (var level in c.Levels.OrderBy(kv => kv.Key))
                    criteria.AppendLine($"- {level.Key}: {level.Value}");
                criteria.AppendLine();
            }

            var values = new Dictionary<string, string>
            {
                ["recruiter_name"] = setup.Persona.Name,
                ["recruiter_role"] = setup.Persona.Role,
                ["student_first_name"] = setup.Student.FirstName,
                ["position_title"] = setup.Position.Title,
                ["company_name"] = setup.Position.Company,
                ["difficulty_name"] = setup.Difficulty.Name,
                ["scale"] = scale,
                ["criteria"] = criteria.ToString().TrimEnd(),
                ["transcript"] = transcript.Trim(),
            };
            return PromptTemplate.Render(_feedbackTemplate, values);
        }

        /// <summary>Limiti di domande della fase, scalati dal livello di difficoltà.</summary>
        public static (int min, int max) PhaseLimits(PhaseConfig phase, DifficultyLevel difficulty)
        {
            int max = Math.Max(1, (int)Math.Ceiling(phase.MaxQuestions * difficulty.QuestionMultiplier - 1e-9));
            int min = Math.Min(Math.Max(1, phase.MinQuestions), max);
            return (min, max);
        }
    }
}
