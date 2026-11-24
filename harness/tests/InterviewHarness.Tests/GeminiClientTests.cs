using System.Net;
using System.Text;
using InterviewHarness.Llm;
using Newtonsoft.Json.Linq;
using Xunit;

namespace InterviewHarness.Tests;

/// <summary>Client Gemini verificato con un server HTTP finto (nessuna chiamata reale).</summary>
public class GeminiClientTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode code, string body)> _responses = new();
        public List<(HttpRequestMessage request, string body)> Requests { get; } = new();

        public FakeHandler(params (HttpStatusCode, string)[] responses)
        {
            foreach (var r in responses) _responses.Enqueue(r);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request, await request.Content!.ReadAsStringAsync(ct)));
            var (code, body) = _responses.Dequeue();
            return new HttpResponseMessage(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static string Ok(string text, string finish = "STOP") => new JObject
    {
        ["candidates"] = new JArray(new JObject
        {
            ["content"] = new JObject
            {
                ["role"] = "model",
                ["parts"] = new JArray(
                    new JObject { ["text"] = "ragionamento interno", ["thought"] = true },
                    new JObject { ["text"] = text }),
            },
            ["finishReason"] = finish,
        }),
        ["usageMetadata"] = new JObject { ["promptTokenCount"] = 120, ["candidatesTokenCount"] = 30, ["thoughtsTokenCount"] = 10 },
    }.ToString();

    private static LlmRequest Request(string? schema = "{\"type\":\"object\"}") => new()
    {
        Purpose = "recruiter_turn",
        Model = "gemini-3.8-flash",
        System = "Sei un recruiter.",
        Messages = { new LlmMessage(LlmRole.User, "Ciao"), new LlmMessage(LlmRole.Assistant, "{}"), new LlmMessage(LlmRole.User, "Sono Luca") },
        MaxTokens = 500,
        Effort = "low",
        JsonSchema = schema,
    };

    [Fact]
    public async Task Richiesta_HaFormatoGenerateContentCorretto()
    {
        var handler = new FakeHandler((HttpStatusCode.OK, Ok("{\"speech\":\"ciao\"}")));
        var client = new GeminiLlmClient("chiave-di-test", new HttpClient(handler));

        await client.CompleteAsync(Request());

        var (req, body) = handler.Requests.Single();
        Assert.EndsWith("/models/gemini-3.8-flash:generateContent", req.RequestUri!.AbsolutePath);
        Assert.Equal("chiave-di-test", req.Headers.GetValues("x-goog-api-key").Single());
        Assert.DoesNotContain("chiave-di-test", req.RequestUri.ToString()); // la chiave non va nell'URL

        var json = JObject.Parse(body);
        Assert.Equal("Sei un recruiter.", (string?)json["systemInstruction"]!["parts"]![0]!["text"]);
        Assert.Equal(new[] { "user", "model", "user" }, json["contents"]!.Select(c => (string?)c["role"]));
        Assert.Equal("application/json", (string?)json["generationConfig"]!["responseMimeType"]);
        Assert.Equal("object", (string?)json["generationConfig"]!["responseJsonSchema"]!["type"]);
        Assert.Equal("LOW", (string?)json["generationConfig"]!["thinkingConfig"]!["thinkingLevel"]);
        Assert.Equal(500, (int)json["generationConfig"]!["maxOutputTokens"]!);
    }

    [Fact]
    public async Task SenzaSchema_NonChiedeJson()
    {
        var handler = new FakeHandler((HttpStatusCode.OK, Ok("Buongiorno")));
        await new GeminiLlmClient("k", new HttpClient(handler)).CompleteAsync(Request(schema: null));
        var json = JObject.Parse(handler.Requests.Single().body);
        Assert.Null(json["generationConfig"]!["responseMimeType"]);
    }

    [Fact]
    public async Task Risposta_IgnoraIlRagionamentoEContaIToken()
    {
        var client = new GeminiLlmClient("k", new HttpClient(new FakeHandler((HttpStatusCode.OK, Ok("{\"speech\":\"ciao\"}")))));

        var response = await client.CompleteAsync(Request());

        Assert.Equal("{\"speech\":\"ciao\"}", response.Text);
        Assert.Equal(120, response.InputTokens);
        Assert.Equal(40, response.OutputTokens);
    }

    [Fact]
    public async Task ErroreTemporaneo_VieneRiprovato()
    {
        var handler = new FakeHandler(
            (HttpStatusCode.ServiceUnavailable, "{\"error\":{\"message\":\"overloaded\"}}"),
            (HttpStatusCode.OK, Ok("ok")));

        var response = await new GeminiLlmClient("k", new HttpClient(handler)).CompleteAsync(Request());

        Assert.Equal("ok", response.Text);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task ErroreDefinitivo_RiportaIlMessaggioDiGemini()
    {
        var handler = new FakeHandler((HttpStatusCode.BadRequest, "{\"error\":{\"message\":\"API key not valid\"}}"));
        var ex = await Assert.ThrowsAsync<HarnessException>(() => new GeminiLlmClient("k", new HttpClient(handler)).CompleteAsync(Request()));
        Assert.Contains("API key not valid", ex.Message);
    }

    [Fact]
    public void RispostaTroncata_OBloccata_DaErroreChiaro()
    {
        Assert.Contains("max_tokens", Assert.Throws<HarnessException>(() => GeminiLlmClient.ParseResponse(Ok("x", "MAX_TOKENS"), "feedback")).Message);
        Assert.Contains("SAFETY", Assert.Throws<HarnessException>(() => GeminiLlmClient.ParseResponse(Ok("x", "SAFETY"), "feedback")).Message);
        Assert.Contains("bloccato", Assert.Throws<HarnessException>(() =>
            GeminiLlmClient.ParseResponse("{\"promptFeedback\":{\"blockReason\":\"SAFETY\"}}", "feedback")).Message);
    }

    [Theory]
    [InlineData("low", "LOW")]
    [InlineData("medium", "MEDIUM")]
    [InlineData("high", "HIGH")]
    [InlineData("max", "XHIGH")]
    public void Effort_DiventaThinkingLevel(string effort, string expected)
    {
        Assert.Equal(expected, GeminiLlmClient.ThinkingLevel(effort));
    }

    [Fact]
    public void SchemiDellaHarness_SonoJsonSchemaValidiPerGemini()
    {
        // Lo schema del recruiter deve entrare così com'è in responseJsonSchema.
        var body = GeminiLlmClient.BuildRequestBody(new LlmRequest
        {
            Model = "gemini-3.8-flash",
            Effort = "low",
            Messages = { new LlmMessage(LlmRole.User, "Ciao") },
            JsonSchema = OutputSchemas.RecruiterTurn(Exchange.ExchangeVocabulary.Emotions, Exchange.ExchangeVocabulary.Gestures),
        });
        var schema = body["generationConfig"]!["responseJsonSchema"]!;
        Assert.False((bool)schema["additionalProperties"]!);
        Assert.Contains("speech", schema["required"]!.Select(t => (string?)t));
    }
}
