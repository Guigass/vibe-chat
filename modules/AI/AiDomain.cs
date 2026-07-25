using System.Net.Http.Json;
using System.Text.Json;
using VibeChat.SharedKernel;

namespace VibeChat.AI;

public sealed class AiUsageRecord
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public decimal CostUsd { get; set; }
    public int LatencyMs { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class AiSettings
{
    public WorkspaceId WorkspaceId { get; set; }
    public TenantId TenantId { get; set; }
    public bool Enabled { get; set; }
    public string Provider { get; set; } = "Mock";
}

public sealed record AiCompletionRequest(string SystemPrompt, string UserPrompt);
public sealed record AiCompletionResponse(string Text, int PromptTokens, int CompletionTokens, int LatencyMs);

/// <summary>Result of channel summarize. When Ok is false, Error is a stable code (e.g. AiDisabled, ProviderError).</summary>
public sealed record SummarizeChannelResult(bool Ok, string Summary, string? Error = null);

/// <summary>Result of suggest-reply. When Ok is false, Error is a stable code (e.g. AiDisabled, ProviderError).</summary>
public sealed record SuggestChannelReplyResult(bool Ok, string Suggestion, string? Error = null);

public interface IAiCompletionProvider
{
    string Name { get; }
    Task<AiCompletionResponse> CompleteAsync(AiCompletionRequest request, CancellationToken cancellationToken);
}

public sealed class NullAiProvider : IAiCompletionProvider
{
    public string Name => "Null";

    public Task<AiCompletionResponse> CompleteAsync(AiCompletionRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new AiCompletionResponse("AI is disabled.", 0, 0, 0));
}

public sealed class MockAiProvider : IAiCompletionProvider
{
    public string Name => "Mock";

    public Task<AiCompletionResponse> CompleteAsync(AiCompletionRequest request, CancellationToken cancellationToken)
    {
        var lines = request.UserPrompt.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var count = Math.Min(lines.Length, 5);
        var isSuggest = request.SystemPrompt.Contains("Suggest", StringComparison.OrdinalIgnoreCase);
        var text = isSuggest
            ? (count == 0
                ? "Thanks for the update — happy to help with the next step."
                : "Mock suggestion: Acknowledge the recent points and propose a clear next step.")
            : (count == 0
                ? "No recent messages to summarize."
                : $"Mock summary: {count} recent messages discuss {string.Join(", ", lines.Take(count).Select((_, i) => $"point {i + 1}"))}.");

        return Task.FromResult(new AiCompletionResponse(text, request.UserPrompt.Length / 4, text.Length / 4, 1));
    }
}

public sealed class OpenRouterAiProvider(HttpClient httpClient) : IAiCompletionProvider
{
    public string Name => "OpenRouter";

    public async Task<AiCompletionResponse> CompleteAsync(AiCompletionRequest request, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        using var response = await httpClient.PostAsJsonAsync("/chat/completions", new
        {
            model = "openai/gpt-4o-mini",
            messages = new[]
            {
                new { role = "system", content = request.SystemPrompt },
                new { role = "user", content = request.UserPrompt }
            }
        }, cancellationToken);

        var latencyMs = (int)(DateTimeOffset.UtcNow - started).TotalMilliseconds;
        if (!response.IsSuccessStatusCode)
        {
            return new AiCompletionResponse("OpenRouter provider is unavailable.", 0, 0, latencyMs);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var text = ExtractChatCompletionText(body);
        return new AiCompletionResponse(text, request.UserPrompt.Length / 4, text.Length / 4, latencyMs);
    }

    public static string ExtractChatCompletionText(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("choices", out var choices)
                && choices.ValueKind == JsonValueKind.Array
                && choices.GetArrayLength() > 0)
            {
                var first = choices[0];
                if (first.TryGetProperty("message", out var message)
                    && message.TryGetProperty("content", out var content)
                    && content.ValueKind == JsonValueKind.String)
                {
                    var text = content.GetString() ?? string.Empty;
                    return text.Length > 4000 ? text[..4000] : text;
                }
            }
        }
        catch (JsonException)
        {
            // Fall through to truncated raw body — never throw from provider parse.
        }

        return json.Length > 512 ? json[..512] : json;
    }
}

public interface ISummarizeChannelFeature
{
    Task<SummarizeChannelResult> SummarizeAsync(TenantId tenantId, WorkspaceId workspaceId, ChannelId channelId, CancellationToken cancellationToken);
}

public interface ISuggestChannelReplyFeature
{
    Task<SuggestChannelReplyResult> SuggestAsync(TenantId tenantId, WorkspaceId workspaceId, ChannelId channelId, CancellationToken cancellationToken);
}
