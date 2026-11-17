using System.Collections.Generic;
using Newtonsoft.Json;

namespace InterviewHarness.Exchange
{
    /// <summary>
    /// Messaggi del formato di scambio Harness ⇄ Unity.
    /// Fonte di verità: docs/formato-scambio.md — tenere allineati.
    /// </summary>
    public static class ExchangeVocabulary
    {
        public const string Version = "0.1";

        public static readonly IReadOnlyList<string> Emotions = new[]
        {
            "neutral", "friendly", "interested", "thoughtful", "skeptical", "pleased", "serious",
        };

        public static readonly IReadOnlyList<string> Gestures = new[]
        {
            "none", "nod", "wave", "handshake", "hand_open", "lean_forward", "lean_back", "take_notes", "think_chin",
        };
    }

    /// <summary>Unity → Harness: avvio del colloquio.</summary>
    public class SessionStart
    {
        [JsonProperty("version")] public string Version { get; set; } = ExchangeVocabulary.Version;
        [JsonProperty("type")] public string Type { get; set; } = "session_start";
        [JsonProperty("student_profile_id")] public string StudentProfileId { get; set; } = "";
        [JsonProperty("position_id")] public string PositionId { get; set; } = "";
        [JsonProperty("recruiter_id")] public string RecruiterId { get; set; } = "";
        [JsonProperty("difficulty")] public string Difficulty { get; set; } = "medio";
    }

    /// <summary>Unity → Harness: risposta dello studente.</summary>
    public class CandidateTurn
    {
        [JsonProperty("version")] public string Version { get; set; } = ExchangeVocabulary.Version;
        [JsonProperty("type")] public string Type { get; set; } = "candidate_turn";
        [JsonProperty("text")] public string Text { get; set; } = "";
        [JsonProperty("silence_seconds", NullValueHandling = NullValueHandling.Ignore)] public double? SilenceSeconds { get; set; }
    }

    /// <summary>Harness → Unity: battuta del recruiter.</summary>
    public class RecruiterTurn
    {
        [JsonProperty("version")] public string Version { get; set; } = ExchangeVocabulary.Version;
        [JsonProperty("type")] public string Type { get; set; } = "recruiter_turn";
        [JsonProperty("turn_id")] public int TurnId { get; set; }
        [JsonProperty("phase")] public string Phase { get; set; } = "";
        [JsonProperty("speech")] public string Speech { get; set; } = "";
        [JsonProperty("emotion")] public string Emotion { get; set; } = "neutral";
        [JsonProperty("gesture")] public string Gesture { get; set; } = "none";
        [JsonProperty("intensity")] public double Intensity { get; set; } = 0.5;
        [JsonProperty("end_of_interview")] public bool EndOfInterview { get; set; }
    }

    /// <summary>Harness → Unity: fine colloquio con il feedback scritto.</summary>
    public class InterviewEnd
    {
        [JsonProperty("version")] public string Version { get; set; } = ExchangeVocabulary.Version;
        [JsonProperty("type")] public string Type { get; set; } = "interview_end";
        [JsonProperty("feedback")] public FeedbackReport Feedback { get; set; } = new FeedbackReport();
    }

    public class FeedbackReport
    {
        [JsonProperty("overall_score")] public double OverallScore { get; set; }
        [JsonProperty("overall_label")] public string OverallLabel { get; set; } = "";
        [JsonProperty("summary")] public string Summary { get; set; } = "";
        [JsonProperty("criteria")] public List<CriterionFeedback> Criteria { get; set; } = new List<CriterionFeedback>();
        [JsonProperty("strengths")] public List<string> Strengths { get; set; } = new List<string>();
        [JsonProperty("improvements")] public List<string> Improvements { get; set; } = new List<string>();
    }

    public class CriterionFeedback
    {
        [JsonProperty("id")] public string Id { get; set; } = "";
        [JsonProperty("name")] public string Name { get; set; } = "";
        [JsonProperty("score")] public int Score { get; set; }
        [JsonProperty("weight")] public double Weight { get; set; }
        [JsonProperty("motivation")] public string Motivation { get; set; } = "";
        [JsonProperty("evidence")] public List<string> Evidence { get; set; } = new List<string>();
        [JsonProperty("suggestion")] public string Suggestion { get; set; } = "";
    }

    public static class ExchangeSerializer
    {
        public static string ToJson(object message, bool indented = false) =>
            JsonConvert.SerializeObject(message, indented ? Formatting.Indented : Formatting.None);

        public static T FromJson<T>(string json) =>
            JsonConvert.DeserializeObject<T>(json) ?? throw new HarnessException("Messaggio JSON vuoto.");
    }
}
