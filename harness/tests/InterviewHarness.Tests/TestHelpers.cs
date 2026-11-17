using InterviewHarness.Config;
using InterviewHarness.Llm;
using InterviewHarness.Session;
using Newtonsoft.Json.Linq;

namespace InterviewHarness.Tests;

/// <summary>Client finto: restituisce risposte predefinite e registra le richieste ricevute.</summary>
public sealed class FakeLlmClient : ILlmClient
{
    private readonly Queue<string> _responses = new();
    private readonly Func<LlmRequest, string>? _generator;

    public List<LlmRequest> Requests { get; } = new();

    public FakeLlmClient(params string[] responses)
    {
        foreach (var r in responses) _responses.Enqueue(r);
    }

    public FakeLlmClient(Func<LlmRequest, string> generator) { _generator = generator; }

    public void Enqueue(string response) => _responses.Enqueue(response);

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        string text = _generator != null ? _generator(request)
            : _responses.Count > 0 ? _responses.Dequeue()
            : throw new InvalidOperationException("FakeLlmClient: nessuna risposta in coda per " + request.Purpose);
        return Task.FromResult(new LlmResponse { Text = text, StopReason = "end_turn", InputTokens = 10, OutputTokens = 5 });
    }
}

public static class TestData
{
    private static readonly Lazy<HarnessConfig> LazyConfig =
        new(() => HarnessConfig.Load(HarnessConfig.FindHarnessRoot(AppContext.BaseDirectory)));

    public static HarnessConfig Config => LazyConfig.Value;

    public static SessionSetup Setup(string difficulty = "medio") =>
        SessionSetup.FromIds(Config, "studente-esempio", "junior-developer", "it-tech-lead", difficulty);

    public static string Turn(string speech, bool goalMet = false, string emotion = "neutral", string gesture = "none",
        params string[] weakPoints) => new JObject
    {
        ["speech"] = speech,
        ["emotion"] = emotion,
        ["gesture"] = gesture,
        ["intensity"] = 0.5,
        ["phase_goal_met"] = goalMet,
        ["weak_points"] = new JArray(weakPoints.Cast<object>().ToArray()),
    }.ToString();

    public static string Feedback(int score = 3, string? skipCriterion = null) => new JObject
    {
        ["summary"] = "Colloquio nel complesso positivo.",
        ["criteria"] = new JArray(Config.Evaluation.Criteria
            .Where(c => c.Id != skipCriterion)
            .Select(c => new JObject
            {
                ["id"] = c.Id,
                ["score"] = score,
                ["motivation"] = "Motivazione di prova.",
                ["evidence"] = new JArray("\"esempio\""),
                ["suggestion"] = "Consiglio di prova.",
            })),
        ["strengths"] = new JArray("Puntuale"),
        ["improvements"] = new JArray("Più esempi"),
    }.ToString();
}
