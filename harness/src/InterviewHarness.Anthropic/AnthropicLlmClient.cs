using System.Text.Json;
using Anthropic.Models.Beta.Messages;
using InterviewHarness.Llm;

namespace InterviewHarness.AnthropicClient;

/// <summary>
/// ILlmClient basato sull'SDK ufficiale Anthropic per C#.
/// Usa l'endpoint beta per attivare i fallback lato server: se il modello principale
/// rifiuta una richiesta, la risposta viene servita da un modello di riserva nella stessa chiamata,
/// così il colloquio non si interrompe.
/// </summary>
public sealed class AnthropicLlmClient : ILlmClient
{
    private const string FallbackBeta = "server-side-fallback-2026-07-01";

    private readonly Anthropic.AnthropicClient _client;

    public AnthropicLlmClient(string? apiKey = null)
    {
        _client = string.IsNullOrEmpty(apiKey) ? new Anthropic.AnthropicClient() : new Anthropic.AnthropicClient { ApiKey = apiKey };
    }

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        var parameters = new MessageCreateParams
        {
            Model = request.Model,
            MaxTokens = request.MaxTokens,
            System = request.System,
            Messages = request.Messages.Select(ToParam).ToList(),
            Betas = [FallbackBeta],
            Fallbacks = new Default(),
            OutputConfig = new BetaOutputConfig
            {
                Effort = ToEffort(request.Effort),
                Format = request.JsonSchema is null ? null : new BetaJsonOutputFormat
                {
                    Schema = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(request.JsonSchema)!,
                },
            },
        };

        BetaMessage response = await _client.Beta.Messages.Create(parameters, cancellationToken);

        if (response.StopReason == "refusal")
            throw new HarnessException($"Il modello ha rifiutato la richiesta ({request.Purpose}).");
        if (response.StopReason == "max_tokens")
            throw new HarnessException($"Risposta troncata: aumentare max_tokens per '{request.Purpose}' in settings.yaml.");

        string text = string.Concat(response.Content.Select(b => b.Value).OfType<BetaTextBlock>().Select(t => t.Text));
        return new LlmResponse
        {
            Text = text,
            StopReason = response.StopReason?.ToString() ?? "",
            InputTokens = response.Usage.InputTokens,
            OutputTokens = response.Usage.OutputTokens,
        };
    }

    private static BetaMessageParam ToParam(LlmMessage m) => m.Role == LlmRole.User
        ? new BetaUserMessageParam { Content = m.Text }
        : new BetaAssistantMessageParam { Content = m.Text };

    private static Effort ToEffort(string effort) => effort.ToLowerInvariant() switch
    {
        "low" => Effort.Low,
        "medium" => Effort.Medium,
        "high" => Effort.High,
        "xhigh" => Effort.Xhigh,
        "max" => Effort.Max,
        _ => throw new HarnessException($"Valore di effort non valido: '{effort}'"),
    };
}
