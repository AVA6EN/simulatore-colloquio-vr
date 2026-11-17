using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace InterviewHarness.Llm
{
    /// <summary>JSON Schema delle risposte del modello (structured outputs).</summary>
    public static class OutputSchemas
    {
        public static string RecruiterTurn(IEnumerable<string> emotions, IEnumerable<string> gestures)
        {
            var schema = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["speech"] = new JObject { ["type"] = "string" },
                    ["emotion"] = new JObject { ["type"] = "string", ["enum"] = new JArray(emotions.Cast<object>().ToArray()) },
                    ["gesture"] = new JObject { ["type"] = "string", ["enum"] = new JArray(gestures.Cast<object>().ToArray()) },
                    ["intensity"] = new JObject { ["type"] = "number" },
                    ["phase_goal_met"] = new JObject { ["type"] = "boolean" },
                    ["weak_points"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string" } },
                },
                ["required"] = new JArray("speech", "emotion", "gesture", "intensity", "phase_goal_met", "weak_points"),
                ["additionalProperties"] = false,
            };
            return schema.ToString(Formatting.None);
        }

        public static string Feedback(IEnumerable<string> criterionIds)
        {
            var stringArray = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string" } };
            var criterion = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["id"] = new JObject { ["type"] = "string", ["enum"] = new JArray(criterionIds.Cast<object>().ToArray()) },
                    ["score"] = new JObject { ["type"] = "integer", ["enum"] = new JArray(1, 2, 3, 4, 5) },
                    ["motivation"] = new JObject { ["type"] = "string" },
                    ["evidence"] = stringArray.DeepClone(),
                    ["suggestion"] = new JObject { ["type"] = "string" },
                },
                ["required"] = new JArray("id", "score", "motivation", "evidence", "suggestion"),
                ["additionalProperties"] = false,
            };
            var schema = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["summary"] = new JObject { ["type"] = "string" },
                    ["criteria"] = new JObject { ["type"] = "array", ["items"] = criterion },
                    ["strengths"] = stringArray.DeepClone(),
                    ["improvements"] = stringArray.DeepClone(),
                },
                ["required"] = new JArray("summary", "criteria", "strengths", "improvements"),
                ["additionalProperties"] = false,
            };
            return schema.ToString(Formatting.None);
        }
    }
}
