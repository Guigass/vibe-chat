using System.Net.Http.Json;
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

public interface IAiCompletionProvider
{
    string Name { get; }
    Task<AiCompletionResponse> CompleteAsync(AiCompletionRequest request, CancellationToken cancellationToken);
}

public sealed class NullAiProvider : IAiCompletionProvider
{
    public string Name => "Null";

    public Task<AiCompletionResponse> CompleteAsync(AiCompletionRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new AiCompletionResponse("AI is disabled for this workspace.", 0, 0, 0));
}

public sealed class MockAiProvider : IAiCompletionProvider
{
    public string Name => "Mock";

    public Task<AiCompletionResponse> CompleteAsync(AiCompletionRequest request, CancellationToken cancellationToken)
    {
        var lines = request.UserPrompt.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var count = Math.Min(lines.Length, 5);
        var text = count == 0
            ? "No recent messages to summarize."
            : $"Mock summary: {count} recent messages discuss {string.Join(", ", lines.Take(count).Select((_, i) => $"point {i + 1}"))}.";

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

        if (!response.IsSuccessStatusCode)
        {
            return new AiCompletionResponse("OpenRouter provider is unavailable.", 0, 0, (int)(DateTimeOffset.UtcNow - started).TotalMilliseconds);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return new AiCompletionResponse(body.Length > 512 ? body[..512] : body, request.UserPrompt.Length / 4, body.Length / 4, (int)(DateTimeOffset.UtcNow - started).TotalMilliseconds);
    }
}

public interface ISummarizeChannelFeature
{
    Task<string> SummarizeAsync(TenantId tenantId, WorkspaceId workspaceId, ChannelId channelId, CancellationToken cancellationToken);
}
