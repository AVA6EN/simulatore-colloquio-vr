using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace InterviewHarness.Llm
{
    /// <summary>
    /// ILlmClient per Google Gemini tramite l'endpoint REST generateContent.
    /// Usa solo HttpClient e Newtonsoft.Json, quindi funziona anche dentro Unity.
    /// Documentazione: https://aistudio.google.com/api-reference/generate-content/generate-content.md
    /// </summary>
    public sealed class GeminiLlmClient : ILlmClient, IDisposable
    {
        public const string DefaultBaseUrl = "https://generativelanguage.googleapis.com/v1beta";

        private const int MaxRetries = 2;

        private readonly HttpClient _http;
        private readonly bool _ownsHttp;
        private readonly string _apiKey;
        private readonly string _baseUrl;

        /// <param name="apiKey">Chiave API di Google AI Studio (GEMINI_API_KEY). Non va mai messa nel repository.</param>
        /// <param name="http">HttpClient opzionale (per i test o per riusarne uno esistente).</param>
        public GeminiLlmClient(string apiKey, HttpClient? http = null, string baseUrl = DefaultBaseUrl)
        {
            if (string.IsNullOrWhiteSpace(apiKey)) throw new HarnessException("Chiave API Gemini mancante.");
            _apiKey = apiKey;
            _baseUrl = baseUrl.TrimEnd('/');
            _ownsHttp = http == null;
            _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        }

        public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
        {
            string body = BuildRequestBody(request).ToString(Formatting.None);
            string url = $"{_baseUrl}/models/{Uri.EscapeDataString(request.Model)}:generateContent";

            for (int attempt = 0; ; attempt++)
            {
                using (var message = new HttpRequestMessage(HttpMethod.Post, url))
                {
                    message.Headers.Add("x-goog-api-key", _apiKey);
                    message.Content = new StringContent(body, Encoding.UTF8, "application/json");

                    using (var response = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false))
                    {
                        string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (response.IsSuccessStatusCode)
                            return ParseResponse(text, request.Purpose);

                        if (IsRetryable(response.StatusCode) && attempt < MaxRetries)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt + 1)), cancellationToken).ConfigureAwait(false);
                            continue;
                        }
                        throw new HarnessException($"Errore Gemini {(int)response.StatusCode} ({request.Purpose}): {ErrorMessage(text)}");
                    }
                }
            }
        }

        /// <summary>Corpo della richiesta generateContent (pubblico per i test).</summary>
        public static JObject BuildRequestBody(LlmRequest request)
        {
            var contents = new JArray(request.Messages.Select(m => new JObject
            {
                ["role"] = m.Role == LlmRole.User ? "user" : "model",
                ["parts"] = new JArray(new JObject { ["text"] = m.Text }),
            }));

            var generationConfig = new JObject
            {
                ["maxOutputTokens"] = request.MaxTokens,
                ["thinkingConfig"] = new JObject { ["thinkingLevel"] = ThinkingLevel(request.Effort) },
            };
            if (request.JsonSchema != null)
            {
                generationConfig["responseMimeType"] = "application/json";
                generationConfig["responseJsonSchema"] = JObject.Parse(request.JsonSchema);
            }

            var body = new JObject
            {
                ["contents"] = contents,
                ["generationConfig"] = generationConfig,
            };
            if (!string.IsNullOrEmpty(request.System))
                body["systemInstruction"] = new JObject { ["parts"] = new JArray(new JObject { ["text"] = request.System }) };
            return body;
        }

        /// <summary>Legge la risposta generateContent (pubblico per i test).</summary>
        public static LlmResponse ParseResponse(string json, string purpose)
        {
            JObject obj;
            try { obj = JObject.Parse(json); }
            catch (JsonException ex) { throw new HarnessException($"Risposta Gemini non valida ({purpose}): {ex.Message}"); }

            string? blocked = (string?)obj["promptFeedback"]?["blockReason"];
            if (!string.IsNullOrEmpty(blocked))
                throw new HarnessException($"Gemini ha bloccato la richiesta ({purpose}): {blocked}");

            var candidate = (obj["candidates"] as JArray)?.FirstOrDefault() as JObject;
            if (candidate == null) throw new HarnessException($"Gemini non ha restituito risposte ({purpose}).");

            string finish = (string?)candidate["finishReason"] ?? "";
            if (finish == "MAX_TOKENS")
                throw new HarnessException($"Risposta troncata: aumentare max_tokens per '{purpose}' in settings.yaml.");
            if (finish != "" && finish != "STOP")
                throw new HarnessException($"Gemini ha interrotto la risposta ({purpose}): {finish}");

            // Le parti con "thought": true sono ragionamento interno, non la risposta.
            var parts = (candidate["content"]?["parts"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>();
            string text = string.Concat(parts.Where(p => p["thought"]?.Type != JTokenType.Boolean || !(bool)p["thought"]!)
                                             .Select(p => (string?)p["text"] ?? ""));

            var usage = obj["usageMetadata"];
            return new LlmResponse
            {
                Text = text,
                StopReason = finish,
                InputTokens = (long?)usage?["promptTokenCount"] ?? 0,
                OutputTokens = ((long?)usage?["candidatesTokenCount"] ?? 0) + ((long?)usage?["thoughtsTokenCount"] ?? 0),
            };
        }

        /// <summary>Converte l'effort della configurazione (low…max) nel thinkingLevel di Gemini.</summary>
        public static string ThinkingLevel(string effort)
        {
            switch ((effort ?? "").ToLowerInvariant())
            {
                case "minimal": return "MINIMAL";
                case "low": return "LOW";
                case "medium": return "MEDIUM";
                case "high": return "HIGH";
                case "xhigh":
                case "max": return "XHIGH";
                default: throw new HarnessException($"Valore di effort non valido: '{effort}'");
            }
        }

        private static bool IsRetryable(HttpStatusCode code) =>
            code == (HttpStatusCode)429 || (int)code >= 500;

        private static string ErrorMessage(string body)
        {
            try { return (string?)JObject.Parse(body)["error"]?["message"] ?? body; }
            catch (JsonException) { return body; }
        }

        public void Dispose()
        {
            if (_ownsHttp) _http.Dispose();
        }
    }
}
