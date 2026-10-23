using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Infrastructure;
using VibeChat.Integrations;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;

namespace VibeChat.Api.Endpoints;

internal static class PluginRegistryEndpoints
{
    internal static void MapPluginRegistry(this RouteGroupBuilder v1)
    {
        v1.MapGet("/admin/workspaces/{workspaceId:guid}/plugin-registry", List)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/admin/workspaces/{workspaceId:guid}/plugin-registry/preview", Preview)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/admin/workspaces/{workspaceId:guid}/plugin-registry/install", Install)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/admin/workspaces/{workspaceId:guid}/plugin-registry/update", Update)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/admin/workspaces/{workspaceId:guid}/plugin-registry/rollback", Rollback)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/admin/workspaces/{workspaceId:guid}/plugin-registry/revoke", Revoke)
            .RequirePermission(Permissions.Workspace.Admin);
    }

    private static async Task<IResult> List(
        Guid workspaceId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock,
        IOptions<PluginRegistryOptions> options, CancellationToken ct)
    {
        if (Blocked(options.Value) is { } blocked) return blocked;
        var gate = await AuthorizeAsync(workspaceId, http, db, tenant, clock, ct);
        if (gate is null) return Results.Forbid();
        var rows = await db.PluginRegistryInstalls.AsNoTracking()
            .Where(x => x.WorkspaceId == gate.Workspace.Id)
            .OrderBy(x => x.PluginId)
            .ToListAsync(ct);
        return Payload(http, StatusCodes.Status200OK, new { installs = rows.Select(row => View(row)).ToArray() });
    }

    private static async Task<IResult> Preview(
        Guid workspaceId, RegistryIndexRequest request, HttpContext http, VibeChatDbContext db, ITenantContext tenant,
        IClock clock, IOptions<PluginRegistryOptions> options, CancellationToken ct)
    {
        if (Blocked(options.Value, request.CatalogUrl) is { } blocked) return blocked;
        if (await AuthorizeAsync(workspaceId, http, db, tenant, clock, ct) is null) return Results.Forbid();
        var verification = PluginRegistryRules.Verify(request.Index, Trust(options.Value));
        if (!verification.Ok)
        {
            return Error(http, StatusCodes.Status400BadRequest, verification.Error ?? PluginRegistryErrors.InvalidIndex);
        }

        return Payload(http, StatusCodes.Status200OK, new { entries = verification.Entries.Select(PreviewOf).ToArray() });
    }

    private static async Task<IResult> Install(
        Guid workspaceId, RegistryChangeRequest request, HttpContext http, VibeChatDbContext db, ITenantContext tenant,
        IAuditWriter audit, IOptions<PluginRegistryOptions> options, IOptions<BotIntegrationOptions> bots,
        IOptions<PluginPlatformOptions> platform, IClock clock, CancellationToken ct)
    {
        if (Blocked(options.Value, request.CatalogUrl) is { } blocked) return blocked;
        if (!bots.Value.Enabled) return IntegrationBotEndpoints.Disabled();
        if (!TryKey(http, request, out var key)) return Error(http, StatusCodes.Status400BadRequest, "IdempotencyKeyRequired");
        var gate = await AuthorizeAsync(workspaceId, http, db, tenant, clock, ct);
        if (gate is null) return Results.Forbid();
        if (!TryEntry(request, options.Value, out var entry, out var error)) return Error(http, StatusCodes.Status400BadRequest, error);
        if (!entry!.Accepted) return Error(http, StatusCodes.Status400BadRequest, entry.Reason ?? PluginRegistryErrors.InvalidSignature);

        var grant = request.GrantCapabilities ?? [];
        if (!PluginRegistryRules.Covers(grant, entry.Capabilities))
        {
            return Error(http, StatusCodes.Status400BadRequest, PluginRegistryErrors.GrantRequired);
        }

        var hash = Hash("install", entry.PluginId, entry.Version, entry.Capabilities, request.AllowDms, request.ChannelIds ?? []);
        var replay = await db.PluginRegistryInstalls.FirstOrDefaultAsync(x => x.WorkspaceId == gate.Workspace.Id && x.IdempotencyKey == key, ct);
        if (replay is not null)
        {
            return replay.RequestHash == hash
                ? Payload(http, StatusCodes.Status200OK, View(replay))
                : Error(http, StatusCodes.Status409Conflict, PluginRegistryErrors.IdempotencyConflict);
        }

        if (await Quarantined(db, gate.Workspace.Id, entry, ct))
        {
            return Error(http, StatusCodes.Status409Conflict, PluginRegistryErrors.VersionRevoked);
        }

        if (!await DependenciesReady(db, gate.Workspace.Id, entry, ct))
        {
            return Error(http, StatusCodes.Status409Conflict, PluginRegistryErrors.DependencyMissing);
        }

        if (!PluginManifestRules.TryParse(entry.ManifestCanonical, out var manifest, out error) || manifest is null)
        {
            return Error(http, StatusCodes.Status400BadRequest, error);
        }

        if (PluginManifestRules.RequiresPlatform(manifest) && !platform.Value.Enabled)
        {
            return Error(http, StatusCodes.Status404NotFound, PluginPlatformErrors.Disabled);
        }

        var channels = await IntegrationBotEndpoints.LoadGrantChannelsAsync(db, gate.Workspace.Id, request.ChannelIds, ct);
        if (channels is null) return Error(http, StatusCodes.Status400BadRequest, "InvalidChannelScope");

        var now = clock.UtcNow;
        var row = await db.PluginRegistryInstalls.FirstOrDefaultAsync(x => x.WorkspaceId == gate.Workspace.Id && x.PluginId == entry.PluginId, ct);
        if (row is { Status: PluginRegistryRules.ActiveStatus })
        {
            return Error(http, StatusCodes.Status409Conflict, PluginManifestRules.AlreadyInstalled);
        }

        var newRow = row is null;

        string? token = null;
        string? last4 = null;
        InstalledPlugin plugin;
        if (row?.InstalledPluginId is Guid existingId)
        {
            plugin = await db.InstalledPlugins.FirstAsync(x => x.Id == existingId && x.WorkspaceId == gate.Workspace.Id, ct);
            ApplyManifest(plugin, manifest, now, enabled: true);
            var bot = await db.IntegrationBots.FirstAsync(x => x.Id == plugin.BotId, ct);
            bot.Enabled = true;
        }
        else
        {
            var count = await db.InstalledPlugins.CountAsync(x => x.WorkspaceId == gate.Workspace.Id, ct);
            if (count >= PluginManifestRules.MaxPluginsPerWorkspace)
            {
                return Error(http, StatusCodes.Status409Conflict, PluginManifestRules.LimitReached);
            }

            var (created, raw, createdLast4) = IntegrationBotEndpoints.ProvisionBot(
                db, gate.Workspace.TenantId, gate.Workspace.Id, manifest.Name, request.AllowDms, now);
            await IntegrationBotEndpoints.ReplaceScopesAsync(db, created, channels, now, ct);
            plugin = NewPlugin(gate.Workspace, manifest, created.Id, now);
            db.InstalledPlugins.Add(plugin);
            token = raw;
            last4 = createdLast4;
        }

        row ??= new PluginRegistryInstall { Id = Guid.NewGuid(), CreatedAt = now };
        if (newRow)
        {
            db.PluginRegistryInstalls.Add(row);
        }

        Copy(row, gate.Workspace, entry, plugin.Id, grant, key, hash, now);
        row.Status = PluginRegistryRules.ActiveStatus;
        row.PreviousPayload = null;
        row.PreviousSignature = null;
        row.PreviousVersion = null;
        audit.Add(Audit(gate, AuditActions.PluginRegistryInstall, entry, now));
        await db.SaveChangesAsync(ct);
        return Results.Json(View(row, token, last4), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> Update(
        Guid workspaceId, RegistryChangeRequest request, HttpContext http, VibeChatDbContext db, ITenantContext tenant,
        IAuditWriter audit, IOptions<PluginRegistryOptions> options, IOptions<BotIntegrationOptions> bots,
        IOptions<PluginPlatformOptions> platform, IClock clock, CancellationToken ct)
    {
        if (Blocked(options.Value, request.CatalogUrl) is { } blocked) return blocked;
        if (!bots.Value.Enabled) return IntegrationBotEndpoints.Disabled();
        if (!TryKey(http, request, out var key)) return Error(http, StatusCodes.Status400BadRequest, "IdempotencyKeyRequired");
        var gate = await AuthorizeAsync(workspaceId, http, db, tenant, clock, ct);
        if (gate is null) return Results.Forbid();
        if (!TryEntry(request, options.Value, out var entry, out var error)) return Error(http, StatusCodes.Status400BadRequest, error);
        if (!entry!.Accepted) return Error(http, StatusCodes.Status400BadRequest, entry.Reason ?? PluginRegistryErrors.InvalidSignature);
        var row = await db.PluginRegistryInstalls.FirstOrDefaultAsync(
            x => x.WorkspaceId == gate.Workspace.Id && x.PluginId == entry.PluginId && x.Status == PluginRegistryRules.ActiveStatus, ct);
        if (row is null) return Error(http, StatusCodes.Status404NotFound, PluginRegistryErrors.NotFound);

        var hash = Hash("update", entry.PluginId, entry.Version, request.ConfirmCapabilities ?? [], false, []);
        if (row.IdempotencyKey == key)
        {
            return row.RequestHash == hash
                ? Payload(http, StatusCodes.Status200OK, View(row))
                : Error(http, StatusCodes.Status409Conflict, PluginRegistryErrors.IdempotencyConflict);
        }

        var compared = PluginRegistryRules.CompareVersion(entry.Version, row.Version);
        if (compared < 0) return Error(http, StatusCodes.Status409Conflict, PluginRegistryErrors.DowngradeRejected);
        if (compared == 0) return Error(http, StatusCodes.Status409Conflict, PluginManifestRules.AlreadyInstalled);
        if (await Quarantined(db, gate.Workspace.Id, entry, ct))
        {
            return Error(http, StatusCodes.Status409Conflict, PluginRegistryErrors.VersionRevoked);
        }

        var added = PluginRegistryRules.AddedCapabilities(row.Capabilities, entry.Capabilities);
        if (!PluginRegistryRules.Covers(request.ConfirmCapabilities ?? [], added))
        {
            return Error(http, StatusCodes.Status409Conflict, PluginRegistryErrors.CapabilityConfirmation);
        }

        if (!await DependenciesReady(db, gate.Workspace.Id, entry, ct))
        {
            return Error(http, StatusCodes.Status409Conflict, PluginRegistryErrors.DependencyMissing);
        }

        if (!PluginManifestRules.TryParse(entry.ManifestCanonical, out var manifest, out error) || manifest is null)
        {
            return Error(http, StatusCodes.Status400BadRequest, error);
        }

        if (PluginManifestRules.RequiresPlatform(manifest) && !platform.Value.Enabled)
        {
            return Error(http, StatusCodes.Status404NotFound, PluginPlatformErrors.Disabled);
        }

        var plugin = await db.InstalledPlugins.FirstOrDefaultAsync(x => x.Id == row.InstalledPluginId && x.WorkspaceId == gate.Workspace.Id, ct);
        if (plugin is null) return Error(http, StatusCodes.Status404NotFound, PluginManifestRules.NotFound);
        var now = clock.UtcNow;
        ApplyManifest(plugin, manifest, now, plugin.Enabled);
        row.PreviousPayload = row.Payload;
        row.PreviousSignature = row.Signature;
        row.PreviousVersion = row.Version;
        Copy(row, gate.Workspace, entry, plugin.Id, row.GrantedCapabilities.Concat(added).Distinct(StringComparer.Ordinal).ToArray(), key, hash, now);
        audit.Add(Audit(gate, AuditActions.PluginRegistryUpdate, entry, now));
        await db.SaveChangesAsync(ct);
        return Payload(http, StatusCodes.Status200OK, View(row));
    }

    private static async Task<IResult> Rollback(
        Guid workspaceId, RegistryChangeRequest request, HttpContext http, VibeChatDbContext db, ITenantContext tenant,
        IAuditWriter audit, IOptions<PluginRegistryOptions> options, IOptions<BotIntegrationOptions> bots, IClock clock, CancellationToken ct)
    {
        if (Blocked(options.Value, request.CatalogUrl) is { } blocked) return blocked;
        if (!bots.Value.Enabled) return IntegrationBotEndpoints.Disabled();
        if (!TryKey(http, request, out var key)) return Error(http, StatusCodes.Status400BadRequest, "IdempotencyKeyRequired");
        var gate = await AuthorizeAsync(workspaceId, http, db, tenant, clock, ct);
        if (gate is null) return Results.Forbid();
        var pluginId = (request.PluginId ?? string.Empty).Trim();
        var row = await db.PluginRegistryInstalls.FirstOrDefaultAsync(x => x.WorkspaceId == gate.Workspace.Id && x.PluginId == pluginId, ct);
        if (row?.PreviousPayload is null || row.PreviousSignature is null || row.PreviousVersion is null)
        {
            return Error(http, StatusCodes.Status404NotFound, PluginRegistryErrors.NotFound);
        }

        var hash = Hash("rollback", pluginId, row.PreviousVersion, [], false, []);
        if (row.IdempotencyKey == key)
        {
            return row.RequestHash == hash
                ? Payload(http, StatusCodes.Status200OK, View(row))
                : Error(http, StatusCodes.Status409Conflict, PluginRegistryErrors.IdempotencyConflict);
        }

        var verification = PluginRegistryRules.Verify(
            PluginRegistryRules.WrapIndex([(row.PreviousPayload, row.PreviousSignature)]), Trust(options.Value));
        var entry = verification.Entries.FirstOrDefault();
        if (!verification.Ok || entry is null || !entry.Accepted)
        {
            return Error(http, StatusCodes.Status400BadRequest, entry?.Reason ?? verification.Error ?? PluginRegistryErrors.InvalidSignature);
        }

        if (await Quarantined(db, gate.Workspace.Id, entry, ct) || !PluginRegistryRules.Covers(row.GrantedCapabilities, entry.Capabilities))
        {
            return Error(http, StatusCodes.Status409Conflict, await Quarantined(db, gate.Workspace.Id, entry, ct)
                ? PluginRegistryErrors.VersionRevoked
                : PluginRegistryErrors.GrantRequired);
        }

        if (!await DependenciesReady(db, gate.Workspace.Id, entry, ct))
        {
            return Error(http, StatusCodes.Status409Conflict, PluginRegistryErrors.DependencyMissing);
        }

        if (!PluginManifestRules.TryParse(entry.ManifestCanonical, out var manifest, out var error) || manifest is null)
        {
            return Error(http, StatusCodes.Status400BadRequest, error);
        }

        var plugin = await db.InstalledPlugins.FirstOrDefaultAsync(x => x.Id == row.InstalledPluginId && x.WorkspaceId == gate.Workspace.Id, ct);
        var bot = plugin is null ? null : await db.IntegrationBots.FirstOrDefaultAsync(x => x.Id == plugin.BotId, ct);
        if (plugin is null || bot is null) return Error(http, StatusCodes.Status404NotFound, PluginManifestRules.NotFound);

        var now = clock.UtcNow;
        var currentPayload = row.Payload;
        var currentSignature = row.Signature;
        var currentVersion = row.Version;
        Copy(row, gate.Workspace, entry, plugin.Id, row.GrantedCapabilities, key, hash, now);
        row.PreviousPayload = currentPayload;
        row.PreviousSignature = currentSignature;
        row.PreviousVersion = currentVersion;
        row.Status = PluginRegistryRules.ActiveStatus;
        ApplyManifest(plugin, manifest, now, enabled: true);
        bot.Enabled = true;
        audit.Add(Audit(gate, AuditActions.PluginRegistryRollback, entry, now));
        await db.SaveChangesAsync(ct);
        return Payload(http, StatusCodes.Status200OK, View(row));
    }

    private static async Task<IResult> Revoke(
        Guid workspaceId, RegistryChangeRequest request, HttpContext http, VibeChatDbContext db, ITenantContext tenant,
        IAuditWriter audit, IOptions<PluginRegistryOptions> options, IClock clock, CancellationToken ct)
    {
        if (Blocked(options.Value, request.CatalogUrl) is { } blocked) return blocked;
        if (!TryKey(http, request, out var key)) return Error(http, StatusCodes.Status400BadRequest, "IdempotencyKeyRequired");
        var gate = await AuthorizeAsync(workspaceId, http, db, tenant, clock, ct);
        if (gate is null) return Results.Forbid();
        var pluginId = (request.PluginId ?? string.Empty).Trim();
        var version = (request.Version ?? string.Empty).Trim();
        if (pluginId.Length == 0 || version.Length == 0)
        {
            return Error(http, StatusCodes.Status400BadRequest, PluginRegistryErrors.InvalidIndex);
        }

        var hash = Hash("revoke", pluginId, version, [], false, []);
        var row = await db.PluginRegistryInstalls.FirstOrDefaultAsync(x => x.WorkspaceId == gate.Workspace.Id && x.PluginId == pluginId, ct);
        if (row?.IdempotencyKey == key)
        {
            return row.RequestHash == hash
                ? Payload(http, StatusCodes.Status200OK, View(row))
                : Error(http, StatusCodes.Status409Conflict, PluginRegistryErrors.IdempotencyConflict);
        }

        var now = clock.UtcNow;
        var quarantine = await db.PluginRegistryQuarantines.FirstOrDefaultAsync(
            x => x.WorkspaceId == gate.Workspace.Id && x.PluginId == pluginId && x.Version == version, ct);
        if (quarantine is null)
        {
            db.PluginRegistryQuarantines.Add(new PluginRegistryQuarantine
            {
                Id = Guid.NewGuid(),
                TenantId = gate.Workspace.TenantId,
                WorkspaceId = gate.Workspace.Id,
                PluginId = pluginId,
                Version = version,
                RevokedAt = now
            });
        }

        if (row is not null && row.Version == version)
        {
            row.Status = PluginRegistryRules.RevokedStatus;
            row.IdempotencyKey = key;
            row.RequestHash = hash;
            row.UpdatedAt = now;
            var plugin = await db.InstalledPlugins.FirstOrDefaultAsync(x => x.Id == row.InstalledPluginId, ct);
            if (plugin is not null)
            {
                plugin.Enabled = false;
                plugin.UpdatedAt = now;
                var bot = await db.IntegrationBots.FirstOrDefaultAsync(x => x.Id == plugin.BotId, ct);
                if (bot is not null) bot.Enabled = false;
            }
        }

        audit.Add(new AuditEvent
        {
            TenantId = gate.Workspace.TenantId,
            ActorUserId = gate.ActorId,
            Action = AuditActions.PluginRegistryRevoke,
            EntityType = "PluginRegistry",
            MetadataJson = JsonSerializer.Serialize(new { workspaceId = gate.Workspace.Id.Value, pluginId, version, status = PluginRegistryRules.RevokedStatus }),
            OccurredAt = now
        });
        await db.SaveChangesAsync(ct);
        return Payload(http, StatusCodes.Status200OK, row is null ? new { pluginId, version, status = PluginRegistryRules.RevokedStatus } : View(row));
    }

    private static async Task<AuthGate?> AuthorizeAsync(
        Guid workspaceId, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IClock clock, CancellationToken ct)
    {
        var profile = await RequestAuth.EnsureProfileAsync(http.User, db, clock, ct);
        var workspace = await RequestAuth.ResolveWorkspaceAsync(new WorkspaceId(workspaceId), profile.Id, db, tenant, ct);
        return workspace is null ? null : new AuthGate(workspace, profile.Id);
    }

    private static bool TryEntry(RegistryChangeRequest request, PluginRegistryOptions options, out RegistryEntryView? entry, out string error)
    {
        entry = null;
        var verification = PluginRegistryRules.Verify(request.Index, Trust(options));
        if (!verification.Ok)
        {
            error = verification.Error ?? PluginRegistryErrors.InvalidIndex;
            return false;
        }

        var pluginId = (request.PluginId ?? string.Empty).Trim();
        var version = (request.Version ?? string.Empty).Trim();
        entry = verification.Entries.FirstOrDefault(item => item.PluginId == pluginId && item.Version == version);
        if (entry is not null)
        {
            error = string.Empty;
            return true;
        }

        error = PluginRegistryErrors.NotFound;
        return false;
    }

    private static async Task<bool> Quarantined(VibeChatDbContext db, WorkspaceId workspaceId, RegistryEntryView entry, CancellationToken ct) =>
        await db.PluginRegistryQuarantines.AnyAsync(
            x => x.WorkspaceId == workspaceId && x.PluginId == entry.PluginId && x.Version == entry.Version, ct);

    private static async Task<bool> DependenciesReady(VibeChatDbContext db, WorkspaceId workspaceId, RegistryEntryView entry, CancellationToken ct)
    {
        if (entry.DependsOn.Length == 0) return true;
        var installed = await db.PluginRegistryInstalls.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && x.Status == PluginRegistryRules.ActiveStatus)
            .Select(x => x.PluginId + "@" + x.Version)
            .ToListAsync(ct);
        return entry.DependsOn.All(item => installed.Contains(item, StringComparer.Ordinal));
    }

    private static void Copy(
        PluginRegistryInstall row, Workspace workspace, RegistryEntryView entry, Guid pluginId, string[] granted,
        string key, string hash, DateTimeOffset now)
    {
        row.TenantId = workspace.TenantId;
        row.WorkspaceId = workspace.Id;
        row.PluginId = entry.PluginId;
        row.Version = entry.Version;
        row.Payload = entry.Payload;
        row.Signature = entry.Signature;
        row.Publisher = entry.Publisher;
        row.Source = entry.Source;
        row.CatalogId = entry.CatalogId;
        row.KeyId = entry.KeyId;
        row.Checksum = entry.Checksum;
        row.ArtifactRef = entry.ArtifactRef;
        row.Provenance = entry.Provenance;
        row.Risk = entry.Risk;
        row.Advisory = entry.Advisory;
        row.Capabilities = entry.Capabilities;
        row.GrantedCapabilities = granted.Order(StringComparer.Ordinal).ToArray();
        row.DependsOn = entry.DependsOn;
        row.InstalledPluginId = pluginId;
        row.IdempotencyKey = key;
        row.RequestHash = hash;
        row.UpdatedAt = now;
        if (row.CreatedAt == default) row.CreatedAt = now;
    }

    private static InstalledPlugin NewPlugin(Workspace workspace, PluginManifestDocument manifest, Guid botId, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = workspace.TenantId,
        WorkspaceId = workspace.Id,
        PluginId = manifest.Id,
        Name = manifest.Name,
        Version = manifest.Version,
        ManifestJson = manifest.CanonicalJson,
        Capabilities = manifest.Capabilities,
        BotId = botId,
        Enabled = true,
        InstalledAt = now,
        UpdatedAt = now,
        Hooks = manifest.Hooks,
        CallbackSecret = new EncryptedSecretEnvelope()
    };

    private static void ApplyManifest(InstalledPlugin plugin, PluginManifestDocument manifest, DateTimeOffset now, bool enabled)
    {
        plugin.Name = manifest.Name;
        plugin.Version = manifest.Version;
        plugin.ManifestJson = manifest.CanonicalJson;
        plugin.Capabilities = manifest.Capabilities;
        plugin.Hooks = manifest.Hooks;
        plugin.Enabled = enabled;
        plugin.UpdatedAt = now;
    }

    private static PluginRegistryTrust Trust(PluginRegistryOptions options) => new(
        options.TrustRoots.Select(root => new RegistryTrustRoot(root.KeyId, root.PublicKeyPem, root.Catalogs ?? [])).ToArray(),
        options.AllowlistedCatalogs ?? [],
        options.Revoked ?? []);

    private static bool TryKey(HttpContext http, RegistryChangeRequest request, out string key)
    {
        var raw = request.IdempotencyKey;
        if (string.IsNullOrWhiteSpace(raw) && http.Request.Headers.TryGetValue("Idempotency-Key", out var header))
        {
            raw = header.ToString();
        }

        return PluginSdkIdempotency.TryNormalize(raw, out key);
    }

    private static string Hash(string operation, string pluginId, string version, string[] granted, bool allowDms, Guid[] channels)
    {
        var text = string.Join('|', new[]
        {
            operation,
            pluginId,
            version,
            string.Join(',', granted.Order(StringComparer.Ordinal)),
            allowDms ? "1" : "0",
            string.Join(',', channels.Order())
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    private static IResult? Blocked(PluginRegistryOptions options, string? catalogUrl = null)
    {
        if (!options.Enabled)
        {
            return Results.Json(new { error = PluginRegistryErrors.Disabled }, statusCode: StatusCodes.Status404NotFound);
        }

        if (PluginRegistryRules.RemoteCatalogBlocked(options.CatalogUrl) || PluginRegistryRules.RemoteCatalogBlocked(catalogUrl))
        {
            return Results.Json(new { error = PluginRegistryErrors.EgressBlocked }, statusCode: StatusCodes.Status400BadRequest);
        }

        return null;
    }

    private static object View(PluginRegistryInstall row, string? token = null, string? last4 = null) => new
    {
        id = row.Id,
        pluginId = row.PluginId,
        version = row.Version,
        previousVersion = row.PreviousVersion,
        publisher = row.Publisher,
        source = row.Source,
        signatureKeyId = row.KeyId,
        signatureFingerprint = PluginRegistryRules.Fingerprint(row.Signature),
        capabilities = row.Capabilities,
        grantedCapabilities = row.GrantedCapabilities,
        risk = row.Risk,
        advisory = row.Advisory,
        provenance = row.Provenance,
        artifactRef = row.ArtifactRef,
        status = row.Status,
        installedPluginId = row.InstalledPluginId,
        token,
        tokenLast4 = last4
    };

    private static object PreviewOf(RegistryEntryView entry) => new
    {
        accepted = entry.Accepted,
        reason = entry.Reason,
        pluginId = entry.PluginId,
        version = entry.Version,
        publisher = entry.Publisher,
        source = entry.Source,
        signatureKeyId = entry.KeyId,
        signatureFingerprint = entry.SignatureFingerprint,
        capabilities = entry.Capabilities,
        dependsOn = entry.DependsOn,
        risk = entry.Risk,
        advisory = entry.Advisory,
        provenance = entry.Provenance,
        artifactRef = entry.ArtifactRef,
        revoked = entry.Revoked
    };

    private static AuditEvent Audit(AuthGate gate, string action, RegistryEntryView entry, DateTimeOffset now) => new()
    {
        TenantId = gate.Workspace.TenantId,
        ActorUserId = gate.ActorId,
        Action = action,
        EntityType = "PluginRegistry",
        MetadataJson = JsonSerializer.Serialize(new
        {
            workspaceId = gate.Workspace.Id.Value,
            pluginId = entry.PluginId,
            version = entry.Version,
            keyId = entry.KeyId,
            status = PluginRegistryRules.ActiveStatus
        }),
        OccurredAt = now
    };

    private static IResult Error(HttpContext http, int status, string error)
    {
        http.Response.Headers.CacheControl = "no-store";
        return Results.Json(new { error }, statusCode: status);
    }

    private static IResult Payload(HttpContext http, int status, object body)
    {
        http.Response.Headers.CacheControl = "no-store";
        return Results.Json(body, statusCode: status);
    }

    private sealed record AuthGate(Workspace Workspace, UserId ActorId);

    private sealed class RegistryIndexRequest
    {
        public string? Index { get; set; }
        public string? CatalogUrl { get; set; }
    }

    private sealed class RegistryChangeRequest
    {
        public string? Index { get; set; }
        public string? CatalogUrl { get; set; }
        public string? PluginId { get; set; }
        public string? Version { get; set; }
        public string[]? GrantCapabilities { get; set; }
        public string[]? ConfirmCapabilities { get; set; }
        public Guid[]? ChannelIds { get; set; }
        public bool AllowDms { get; set; }
        public string? IdempotencyKey { get; set; }
    }
}
