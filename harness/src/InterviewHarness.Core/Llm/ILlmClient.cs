using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace InterviewHarness.Llm
{
    public enum LlmRole { User, Assistant }

    public class LlmMessage
    {
        public LlmRole Role { get; set; }
        public string Text { get; set; } = "";

        public LlmMessage() { }
        public LlmMessage(LlmRole role, string text) { Role = role; Text = text; }
    }

    public class LlmRequest
    {
        /// <summary>A cosa serve la richiesta (es. "recruiter_turn", "feedback"): utile per log e test.</summary>
        public string Purpose { get; set; } = "";
        public string Model { get; set; } = "";
        public string System { get; set; } = "";
        public List<LlmMessage> Messages { get; set; } = new List<LlmMessage>();
        public int MaxTokens { get; set; } = 2000;
        /// <summary>low | medium | high | xhigh | max</summary>
        public string Effort { get; set; } = "low";
        /// <summary>JSON Schema (come stringa JSON) che la risposta deve rispettare, o null per testo libero.</summary>
        public string? JsonSchema { get; set; }
    }

    public class LlmResponse
    {
        public string Text { get; set; } = "";
        public string StopReason { get; set; } = "";
        public long InputTokens { get; set; }
        public long OutputTokens { get; set; }
    }

    /// <summary>
    /// Astrazione della chiamata al modello. La harness dipende solo da questa interfaccia:
    /// - GeminiLlmClient (in questa libreria) chiama Google Gemini via HTTP: è quello usato di default,
    ///   anche in Unity;
    /// - InterviewHarness.Anthropic la implementa con l'SDK ufficiale di Claude (alternativa);
    /// - nei test unitari si usa un client finto con risposte predefinite.
    /// </summary>
    public interface ILlmClient
    {
        Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default);
    }
}
