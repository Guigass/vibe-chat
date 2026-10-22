using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using VibeChat.Integrations;

namespace VibeChat.Infrastructure;

/// <summary>
/// SDK catalog and synthetic contract runner (B-136). No network call and no token log.
/// </summary>
public sealed class PluginSdkService(IOptions<PluginSdkOptions> options)
{
    public bool Enabled => options.Value.Enabled;

    public JsonObject Catalog(DateTimeOffset now) => PluginSdkCatalog.Document(now);

    public JsonObject Verify(
        string? body,
        string? signature,
        string? deliveryId,
        DateTimeOffset? deliveredAt,
        IReadOnlyCollection<string>? seenDeliveryIds,
        DateTimeOffset now) =>
        PluginSdkVerifier.ToJson(PluginSdkVerifier.Verify(
            PluginSdkCatalog.SandboxSecret,
            body ?? string.Empty,
            signature,
            deliveryId,
            deliveredAt ?? now,
            now,
            seenDeliveryIds));

    public JsonObject ContractReport(DateTimeOffset now) => PluginSdkContractRunner.Report(now);

    public SdkSandboxDecision Sandbox(string? operation, string? idempotencyKey) =>
        PluginSdkClient.AcceptSandbox(operation, idempotencyKey);
}
