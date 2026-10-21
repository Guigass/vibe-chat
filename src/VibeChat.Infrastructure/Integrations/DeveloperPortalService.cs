using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VibeChat.Integrations;
using VibeChat.SharedKernel;

namespace VibeChat.Infrastructure;

/// <summary>Read models for the developer portal (B-135). Mutations stay in the API.</summary>
public sealed class DeveloperPortalService(
    IOptions<DeveloperPortalOptions> options,
    IOptions<BotIntegrationOptions> bots,
    VibeChatDbContext db,
    RateLimitSettingsResolver limits)
{
    public bool Enabled => options.Value.Enabled;

    public JsonObject Catalog() => DeveloperPortalCatalog.Catalog(bots.Value.Enabled);

    public JsonObject OpenApi() => DeveloperPortalCatalog.OpenApiDocument();

    public JsonObject ContractKit() => DeveloperPortalCatalog.ContractKit(bots.Value.Enabled);

    public async Task<IReadOnlyList<DeveloperCredentialView>> ListAsync(WorkspaceId workspaceId, CancellationToken ct)
    {
        var rows = await db.IntegrationBots.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);
        var views = new List<DeveloperCredentialView>(rows.Count);
        foreach (var bot in rows)
        {
            views.Add(await ViewAsync(bot, ct));
        }

        return views;
    }

    public async Task<JsonObject> UsageAsync(TenantId tenantId, WorkspaceId workspaceId, CancellationToken ct)
    {
        var rate = await limits.ResolveAsync(tenantId, ct);
        var workspaceKey = workspaceId.Value.ToString("D");
        var audits = await db.AuditEvents.AsNoTracking()
            .Where(x => x.Action.StartsWith("developer."))
            .OrderByDescending(x => x.OccurredAt)
            .Take(100)
            .ToListAsync(ct);

        var calls = new JsonArray();
        var errors = 0;
        foreach (var audit in audits)
        {
            if (!audit.MetadataJson.Contains(workspaceKey, StringComparison.Ordinal))
            {
                continue;
            }

            if (!TryReadCall(audit.Action, audit.MetadataJson, audit.OccurredAt, out var call, out var status))
            {
                continue;
            }

            if (DeveloperPortalCatalog.ContainsSecret(call.ToJsonString()))
            {
                continue;
            }

            if (status >= 400)
            {
                errors++;
            }

            calls.Add(call);
            if (calls.Count == 40)
            {
                break;
            }
        }

        return new JsonObject
        {
            ["sendPerMinute"] = rate.SendPerMinute,
            ["window"] = "1m",
            ["scope"] = "credential",
            ["errors"] = errors,
            ["calls"] = calls
        };
    }

    public Task<DeveloperCredentialView> ViewAsync(IntegrationBot bot, CancellationToken ct) => ViewCoreAsync(bot, ct);

    private async Task<DeveloperCredentialView> ViewCoreAsync(IntegrationBot bot, CancellationToken ct)
    {
        var channelIds = await db.IntegrationBotChannelScopes.AsNoTracking()
            .Where(x => x.BotId == bot.Id)
            .Select(x => x.ChannelId.Value)
            .ToArrayAsync(ct);
        var token = await db.IntegrationBotTokens.AsNoTracking()
            .Where(x => x.BotId == bot.Id)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);
        var active = token is { RevokedAt: null };
        return new DeveloperCredentialView(
            bot.Id,
            bot.Name,
            bot.Enabled,
            bot.AllowDms,
            channelIds,
            active,
            token?.Last4,
            token?.LastUsedAt,
            !active,
            bot.CreatedAt);
    }

    private static bool TryReadCall(string action, string metadata, DateTimeOffset occurredAt, out JsonObject call, out int status)
    {
        call = new JsonObject();
        status = 0;
        try
        {
            using var document = JsonDocument.Parse(metadata);
            var root = document.RootElement;
            status = root.TryGetProperty("status", out var statusValue) && statusValue.TryGetInt32(out var code) ? code : 200;
            call = new JsonObject
            {
                ["action"] = action,
                ["status"] = status,
                ["occurredAt"] = occurredAt,
                ["synthetic"] = root.TryGetProperty("synthetic", out var synthetic) && synthetic.ValueKind == JsonValueKind.True,
                ["credentialId"] = root.TryGetProperty("credentialId", out var credential) ? credential.GetString() : null,
                ["operation"] = root.TryGetProperty("operation", out var operation) ? operation.GetString() : null
            };
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
