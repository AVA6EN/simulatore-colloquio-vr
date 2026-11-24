using System.Collections.Generic;

namespace InterviewHarness.Config
{
    /// <summary>harness/config/settings.yaml</summary>
    public class Settings
    {
        /// <summary>gemini | anthropic</summary>
        public string Provider { get; set; } = "gemini";
        public string Model { get; set; } = "gemini-3.8-flash";
        public string TurnEffort { get; set; } = "low";
        public string FeedbackEffort { get; set; } = "high";
        public int TurnMaxTokens { get; set; } = 2000;
        public int FeedbackMaxTokens { get; set; } = 8000;
        public int MaxGuardRetries { get; set; } = 2;
        public string Language { get; set; } = "it";
    }

    /// <summary>Una voce di harness/config/phases.yaml</summary>
    public class PhaseConfig
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Goal { get; set; } = "";
        public int MinQuestions { get; set; } = 1;
        public int MaxQuestions { get; set; } = 2;
        public string Prompt { get; set; } = "";
        public string DefaultEmotion { get; set; } = "neutral";
    }

    public class PhasesFile
    {
        public List<PhaseConfig> Phases { get; set; } = new List<PhaseConfig>();
    }

    public class RecruiterPersona
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Role { get; set; } = "";
        public string Sector { get; set; } = "";
        public string Style { get; set; } = "";
        public List<string> Focus { get; set; } = new List<string>();
    }

    public class DifficultyLevel
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public double QuestionMultiplier { get; set; } = 1.0;
        public string Behavior { get; set; } = "";
    }

    /// <summary>harness/config/recruiters.yaml</summary>
    public class RecruitersFile
    {
        public List<RecruiterPersona> Personas { get; set; } = new List<RecruiterPersona>();
        public List<DifficultyLevel> DifficultyLevels { get; set; } = new List<DifficultyLevel>();
    }

    public class EvaluationCriterion
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public double Weight { get; set; }
        public string Description { get; set; } = "";
        public Dictionary<int, string> Levels { get; set; } = new Dictionary<int, string>();
    }

    /// <summary>harness/config/evaluation.yaml</summary>
    public class EvaluationGrid
    {
        public Dictionary<int, string> Scale { get; set; } = new Dictionary<int, string>();
        public List<EvaluationCriterion> Criteria { get; set; } = new List<EvaluationCriterion>();
    }

    public class ForbiddenTopic
    {
        public string Id { get; set; } = "";
        public List<string> Patterns { get; set; } = new List<string>();
    }

    public class PiiRule
    {
        public string Id { get; set; } = "";
        public string Pattern { get; set; } = "";
        public string Replacement { get; set; } = "";
    }

    /// <summary>harness/config/guardrails.yaml</summary>
    public class GuardrailsConfig
    {
        public int MaxSpeechWords { get; set; } = 60;
        public int MaxQuestionsPerTurn { get; set; } = 1;
        public List<string> RoleBreakPhrases { get; set; } = new List<string>();
        public List<ForbiddenTopic> ForbiddenTopics { get; set; } = new List<ForbiddenTopic>();
        public List<PiiRule> PiiRedaction { get; set; } = new List<PiiRule>();
        public string FallbackSpeech { get; set; } = "Scusami, puoi dirmi qualcosa in più su quello che hai appena detto?";
    }

    /// <summary>harness/data/students/*.yaml</summary>
    public class StudentProfile
    {
        public string Id { get; set; } = "";
        public string FirstName { get; set; } = "";
        public string Course { get; set; } = "";
        public string Cv { get; set; } = "";
    }

    /// <summary>harness/data/positions/*.yaml</summary>
    public class JobPosition
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Company { get; set; } = "";
        public string Sector { get; set; } = "";
        public string Description { get; set; } = "";
    }
}
