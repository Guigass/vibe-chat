using System.Diagnostics.Metrics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VibeChat.Administration;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Integrations;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;

namespace VibeChat.Infrastructure.Compliance;

public sealed record SiemCommandResult(int Status, string? Error, object? Body)
{
    public static SiemCommandResult Ok(object body, int status = 200) => new(status, null, body);

    public static SiemCommandResult Fail(int status, string error) => new(status, error, null);
}

/// <summary>
/// Test seam for the SIEM HTTP client. Production leaves <see cref="Send"/> null.
/// </summary>
public static class SiemTestDoubles
{
    public static Func<ConnectorHttpRequest, ConnectorHttpResult>? Send { get; set; }
}

public sealed class SiemExportService(
    VibeChatDbContext db,
    ITenantContext tenant,
    IClock clock,
    IOptions<SiemOptions> options,
    IAuditWriter audit,
    RuntimeSecretProtector secrets,
    IConnectorAddressLookup lookup,
    IHttpClientFactory httpClientFactory,
    IServiceScopeFactory scopes,
    ILogger<SiemExportService> logger)
{
    public const string HttpClientName = "vibechat-siem";

    private static readonly Meter Meter = new("VibeChat.Siem", "1.0.0");
    private static readonly Counter<long> Deliveries = Meter.CreateCounter<long>("vibechat.siem.deliveries");
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<SiemCommandResult> ListAsync(Workspace workspace, CancellationToken cancellationToken)
    {
        if (!Enabled())
        {
            return SiemCommandResult.Fail(404, SiemErrors.Disabled);
        }

        var rows = await db.SiemDestinations.AsNoTracking()
            .Where(x => x.WorkspaceId == workspace.Id)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
        var now = clock.UtcNow;
        var views = new List<object>(rows.Count);
        foreach (var row in rows)
        {
            views.Add(await ViewAsync(row, now, null, cancellationToken));
        }

        return SiemCommandResult.Ok(views);
    }

    public async Task<SiemCommandResult> CreateAsync(
        Workspace workspace,
        UserId actor,
        string? name,
        string? url,
        string? transport,
        IReadOnlyList<string>? categories,
        string? minSeverity,
        bool? minimize,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!Enabled())
        {
            return SiemCommandResult.Fail(404, SiemErrors.Disabled);
        }

        if (!TryName(name, out var trimmedName))
        {
            return SiemCommandResult.Fail(400, SiemErrors.NameInvalid);
        }

        if (!SiemTransports.Known(transport))
        {
            return SiemCommandResult.Fail(400, SiemErrors.TransportInvalid);
        }

        if (!SiemRules.TryCategories(categories, out var categoryList, out var categoryError))
        {
            return SiemCommandResult.Fail(400, categoryError);
        }

        var severity = string.IsNullOrWhiteSpace(minSeverity) ? SiemSeverities.Info : minSeverity.Trim();
        if (!SiemSeverities.Known(severity))
        {
            return SiemCommandResult.Fail(400, SiemErrors.SeverityInvalid);
        }

        var replay = await db.SiemDestinations
            .FirstOrDefaultAsync(x => x.WorkspaceId == workspace.Id && x.IdempotencyKey == idempotencyKey, cancellationToken);
        if (replay is not null)
        {
            return SiemCommandResult.Ok(await ViewAsync(replay, clock.UtcNow, null, cancellationToken));
        }

        var active = await db.SiemDestinations.CountAsync(
            x => x.WorkspaceId == workspace.Id && x.Status == SiemStatuses.Active, cancellationToken);
        if (active >= SiemLimits.MaxDestinations)
        {
            return SiemCommandResult.Fail(409, SiemErrors.Limit);
        }

        if (!TryEgress(url, out var destinationUrl, out var egressError))
        {
            return SiemCommandResult.Fail(400, egressError);
        }

        var now = clock.UtcNow;
        var secret = NewSecret();
        var row = new SiemDestination
        {
            Id = Guid.NewGuid(),
            TenantId = workspace.TenantId,
            WorkspaceId = workspace.Id,
            Name = trimmedName,
            Transport = transport!,
            Url = destinationUrl!.AbsoluteUri,
            CategoriesJson = JsonSerializer.Serialize(categoryList),
            MinSeverity = severity,
            Minimize = minimize ?? true,
            Status = SiemStatuses.Active,
            Circuit = SiemCircuits.Closed,
            CheckpointAt = now,
            IdempotencyKey = idempotencyKey,
            CreatedAt = now
        };
        row.SigningSecret = secrets.Protect(
            secret, RuntimeSecretKinds.SiemSigningSecret, row.TenantId, row.WorkspaceId, row.Id.ToString("N"), now);
        db.SiemDestinations.Add(row);
        WriteAudit(workspace, actor, AuditActions.SiemDestinationCreate, row.Id, new { transport });
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("SIEM destination {DestinationId} created", row.Id);
        return SiemCommandResult.Ok(await ViewAsync(row, now, secret, cancellationToken), 201);
    }

    public async Task<SiemCommandResult> RotateAsync(
        Workspace workspace,
        UserId actor,
        Guid destinationId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var row = await FindAsync(workspace, destinationId, cancellationToken);
        if (row is null)
        {
            return await MissingOrDisabled(workspace, destinationId, cancellationToken);
        }

        if (row.Status != SiemStatuses.Active)
        {
            return SiemCommandResult.Fail(409, SiemErrors.Revoked);
        }

        if (string.Equals(row.RotateKey, idempotencyKey, StringComparison.Ordinal))
        {
            return SiemCommandResult.Ok(await ViewAsync(row, clock.UtcNow, null, cancellationToken));
        }

        var now = clock.UtcNow;
        var secret = NewSecret();
        row.SigningSecret = secrets.Protect(
            secret, RuntimeSecretKinds.SiemSigningSecret, row.TenantId, row.WorkspaceId, row.Id.ToString("N"), now);
        row.RotateKey = idempotencyKey;
        row.Circuit = SiemCircuits.Closed;
        row.FailureCount = 0;
        row.OpenUntil = null;
        row.NextAttemptAt = null;
        row.LastError = null;
        WriteAudit(workspace, actor, AuditActions.SiemDestinationRotate, row.Id, new { rotated = true });
        await db.SaveChangesAsync(cancellationToken);
        return SiemCommandResult.Ok(await ViewAsync(row, now, secret, cancellationToken));
    }

    public async Task<SiemCommandResult> RevokeAsync(
        Workspace workspace,
        UserId actor,
        Guid destinationId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var row = await FindAsync(workspace, destinationId, cancellationToken);
        if (row is null)
        {
            return await MissingOrDisabled(workspace, destinationId, cancellationToken);
        }

        if (row.Status == SiemStatuses.Revoked)
        {
            return SiemCommandResult.Ok(await ViewAsync(row, clock.UtcNow, null, cancellationToken));
        }

        row.Status = SiemStatuses.Revoked;
        row.RevokedAt = clock.UtcNow;
        row.RotateKey = idempotencyKey;
        WriteAudit(workspace, actor, AuditActions.SiemDestinationRevoke, row.Id, new { revoked = true });
        await db.SaveChangesAsync(cancellationToken);
        return SiemCommandResult.Ok(await ViewAsync(row, row.RevokedAt.Value, null, cancellationToken));
    }

    public async Task<SiemCommandResult> TestAsync(
        Workspace workspace,
        UserId actor,
        Guid destinationId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var row = await FindAsync(workspace, destinationId, cancellationToken);
        if (row is null)
        {
            return await MissingOrDisabled(workspace, destinationId, cancellationToken);
        }

        if (row.Status != SiemStatuses.Active)
        {
            return SiemCommandResult.Fail(409, SiemErrors.Revoked);
        }

        if (string.Equals(row.TestKey, idempotencyKey, StringComparison.Ordinal) && row.TestEventId is Guid previous)
        {
            return SiemCommandResult.Ok(new { eventId = previous, synthetic = true, circuit = row.Circuit });
        }

        var now = clock.UtcNow;
        if (!SiemRules.MaySend(row.Status, row.Circuit, row.OpenUntil, row.NextAttemptAt, now))
        {
            return SiemCommandResult.Fail(409, SiemErrors.CircuitOpen);
        }

        var eventId = Guid.NewGuid();
        var envelope = new SiemEnvelope(eventId, now, "siem", SiemSeverities.Info, AuditActions.SiemTest, true, "SiemDestination", row.Id.ToString("D"), actor.Value, null);
        var sent = await TransmitAsync(row, eventId, envelope, now, cancellationToken);
        if (!sent)
        {
            await db.SaveChangesAsync(cancellationToken);
            return SiemCommandResult.Fail(409, row.Circuit == SiemCircuits.Open ? SiemErrors.CircuitOpen : row.LastError ?? SiemErrors.EgressBlocked);
        }

        row.TestKey = idempotencyKey;
        row.TestEventId = eventId;
        WriteAudit(workspace, actor, AuditActions.SiemTest, row.Id, new { synthetic = true });
        await db.SaveChangesAsync(cancellationToken);
        return SiemCommandResult.Ok(new { eventId, synthetic = true, circuit = row.Circuit });
    }

    public async Task<SiemCommandResult> BackfillAsync(
        Workspace workspace,
        UserId actor,
        Guid destinationId,
        DateTimeOffset? since,
        int? limit,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var row = await FindAsync(workspace, destinationId, cancellationToken);
        if (row is null)
        {
            return await MissingOrDisabled(workspace, destinationId, cancellationToken);
        }

        if (row.Status != SiemStatuses.Active)
        {
            return SiemCommandResult.Fail(409, SiemErrors.Revoked);
        }

        if (string.Equals(row.BackfillKey, idempotencyKey, StringComparison.Ordinal))
        {
            return SiemCommandResult.Ok(new { count = row.BackfillCount, replay = true });
        }

        var now = clock.UtcNow;
        if (!SiemRules.TryBackfill(since, limit, now, out var start, out var take, out var error))
        {
            return SiemCommandResult.Fail(400, error);
        }

        var delivered = db.SiemDeliveries
            .Where(x => x.DestinationId == row.Id && x.Status == SiemDeliveryStatuses.Delivered)
            .Select(x => x.EventId);
        var events = await WithCategories(
                db.AuditEvents.AsNoTracking()
                    .Where(x => x.TenantId == workspace.TenantId && x.OccurredAt >= start && x.OccurredAt <= now && !delivered.Contains(x.Id)),
                ReadCategories(row.CategoriesJson))
            .OrderBy(x => x.OccurredAt)
            .ThenBy(x => x.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

        var count = 0;
        foreach (var auditEvent in events)
        {
            if (!await SendAuditAsync(row, auditEvent, advanceCheckpoint: false, cancellationToken))
            {
                break;
            }

            count++;
        }

        row.BackfillKey = idempotencyKey;
        row.BackfillCount = count;
        WriteAudit(workspace, actor, AuditActions.SiemBackfill, row.Id, new { count, limit = take });
        await db.SaveChangesAsync(cancellationToken);
        return SiemCommandResult.Ok(new { count, limit = take });
    }

    public async Task<int> DrainAsync(CancellationToken cancellationToken)
    {
        if (!Enabled())
        {
            return 0;
        }

        tenant.SetJobRole(SiemLimits.JobRole);
        await RlsSession.EnsureAppliedAsync(db, tenant, cancellationToken);
        var claimed = await db.SiemDestinations.IgnoreQueryFilters()
            .Where(x => x.Status == SiemStatuses.Active)
            .Select(x => new { x.Id, x.TenantId })
            .ToListAsync(cancellationToken);
        tenant.SetJobRole(null);
        await RlsSession.CommitAsync(db, cancellationToken);

        var sent = 0;
        foreach (var item in claimed)
        {
            using var scope = scopes.CreateScope();
            var worker = scope.ServiceProvider.GetRequiredService<SiemExportService>();
            sent += await worker.DeliverLiveAsync(item.TenantId, item.Id, cancellationToken);
        }

        return sent;
    }

    public async Task<int> DeliverLiveAsync(TenantId tenantId, Guid destinationId, CancellationToken cancellationToken)
    {
        if (!Enabled())
        {
            return 0;
        }

        tenant.SetTenant(tenantId);
        tenant.SetJobRole(null);
        await RlsSession.EnsureAppliedAsync(db, tenant, cancellationToken);
        var row = await db.SiemDestinations.FirstOrDefaultAsync(x => x.Id == destinationId, cancellationToken);
        if (row is null || row.Status != SiemStatuses.Active)
        {
            await RlsSession.CommitAsync(db, cancellationToken);
            return 0;
        }

        var now = clock.UtcNow;
        if (!SiemRules.MaySend(row.Status, row.Circuit, row.OpenUntil, row.NextAttemptAt, now))
        {
            await RlsSession.CommitAsync(db, cancellationToken);
            return 0;
        }

        var events = await LiveQuery(row).Take(SiemLimits.DrainBatch).ToListAsync(cancellationToken);
        var sent = 0;
        foreach (var auditEvent in events)
        {
            if (!await SendAuditAsync(row, auditEvent, advanceCheckpoint: true, cancellationToken))
            {
                break;
            }

            sent++;
        }

        await db.SaveChangesAsync(cancellationToken);
        await RlsSession.CommitAsync(db, cancellationToken);
        return sent;
    }

    private IQueryable<AuditEvent> LiveQuery(SiemDestination row)
    {
        var at = row.CheckpointAt;
        var id = row.CheckpointEventId;
        var query = db.AuditEvents.AsNoTracking().Where(x => x.OccurredAt > at);
        if (id is Guid checkpointId)
        {
            query = db.AuditEvents.AsNoTracking().Where(x => x.OccurredAt > at || (x.OccurredAt == at && x.Id > checkpointId));
        }

        return query.OrderBy(x => x.OccurredAt).ThenBy(x => x.Id);
    }

    private async Task<bool> SendAuditAsync(
        SiemDestination row,
        AuditEvent auditEvent,
        bool advanceCheckpoint,
        CancellationToken cancellationToken)
    {
        var existing = await db.SiemDeliveries
            .FirstOrDefaultAsync(x => x.DestinationId == row.Id && x.EventId == auditEvent.Id, cancellationToken);
        if (existing?.Status == SiemDeliveryStatuses.Delivered)
        {
            if (advanceCheckpoint)
            {
                Advance(row, auditEvent);
            }

            return true;
        }

        var (category, severity) = SiemRules.Classify(auditEvent.Action);
        var categories = ReadCategories(row.CategoriesJson);
        if (!SiemRules.Allows(categories, row.MinSeverity, category, severity))
        {
            await UpsertAsync(existing, row, auditEvent.Id, SiemDeliveryStatuses.Skipped, null, cancellationToken);
            if (advanceCheckpoint)
            {
                Advance(row, auditEvent);
            }

            Deliveries.Add(1, new KeyValuePair<string, object?>("result", "skipped"));
            return true;
        }

        var now = clock.UtcNow;
        if (!SiemRules.MaySend(row.Status, row.Circuit, row.OpenUntil, row.NextAttemptAt, now))
        {
            return false;
        }

        string? secret = Unprotect(row);
        var envelope = new SiemEnvelope(
            auditEvent.Id,
            auditEvent.OccurredAt,
            category,
            severity,
            auditEvent.Action,
            false,
            auditEvent.EntityType,
            auditEvent.EntityId,
            auditEvent.ActorUserId?.Value,
            SiemRules.RedactMetadata(auditEvent.MetadataJson, row.Minimize || row.Transport == SiemTransports.SyslogCef, secret));
        if (secret is null)
        {
            ApplyCircuit(row, SiemRules.Record(0, row.FailureCount, now), "SecretUnavailable");
            await UpsertAsync(existing, row, auditEvent.Id, SiemDeliveryStatuses.Failed, "SecretUnavailable", cancellationToken);
            return false;
        }

        var sent = await TransmitAsync(row, auditEvent.Id, envelope, now, cancellationToken);
        if (!sent)
        {
            await UpsertAsync(existing, row, auditEvent.Id, SiemDeliveryStatuses.Failed, row.LastError, cancellationToken);
            return false;
        }

        await UpsertAsync(existing, row, auditEvent.Id, SiemDeliveryStatuses.Delivered, null, cancellationToken);
        if (advanceCheckpoint)
        {
            Advance(row, auditEvent);
        }

        return true;
    }

    private async Task<bool> TransmitAsync(
        SiemDestination row,
        Guid eventId,
        SiemEnvelope envelope,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!TryEgress(row.Url, out var uri, out var egressError))
        {
            ApplyCircuit(row, SiemRules.Record(0, row.FailureCount, now), egressError);
            return false;
        }

        var secret = Unprotect(row);
        if (secret is null)
        {
            ApplyCircuit(row, SiemRules.Record(0, row.FailureCount, now), "SecretUnavailable");
            return false;
        }

        var body = row.Transport == SiemTransports.SyslogCef
            ? SiemRules.BuildCef(envelope)
            : SiemRules.BuildJson(envelope);
        if (row.Transport == SiemTransports.WebhookJson)
        {
            var decision = PublicEventCatalog.Accept(SiemLimits.EventType, body);
            if (!decision.Accepted)
            {
                ApplyCircuit(row, SiemRules.Record(0, row.FailureCount, now), decision.Reason);
                logger.LogWarning("SIEM envelope rejected for {DestinationId}: {Reason}", row.Id, decision.Reason);
                return false;
            }
        }

        var signature = WebhookDelivery.ComputeSignature(secret, body);
        var contentType = row.Transport == SiemTransports.SyslogCef ? "text/plain" : "application/json";
        var headers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [WebhookDelivery.SignatureHeader] = signature,
            [WebhookDelivery.EventHeader] = SiemLimits.EventType,
            [WebhookDelivery.SchemaVersionHeader] = SiemLimits.SchemaVersion.ToString(),
            [WebhookDelivery.DeliveryIdHeader] = eventId.ToString("D")
        };
        var result = await PostAsync(uri!, body, contentType, headers, cancellationToken);
        var state = SiemRules.Record(result.StatusCode, row.FailureCount, now);
        var error = result.StatusCode is >= 200 and < 300 ? null : result.ErrorCode ?? $"Http{result.StatusCode}";
        ApplyCircuit(row, state, error);
        if (error is null)
        {
            row.LastSuccessAt = now;
            Deliveries.Add(1, new KeyValuePair<string, object?>("result", "delivered"));
            return true;
        }

        Deliveries.Add(1, new KeyValuePair<string, object?>("result", "failed"));
        logger.LogWarning("SIEM delivery {DestinationId} failed with {Error}", row.Id, error);
        return false;
    }

    private async Task<ConnectorHttpResult> PostAsync(
        Uri url,
        string body,
        string contentType,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken)
    {
        var request = new ConnectorHttpRequest(HttpMethod.Post.Method, url, body, contentType, headers);
        if (SiemTestDoubles.Send is { } send)
        {
            return send(request);
        }

        var client = httpClientFactory.CreateClient(HttpClientName);
        using var message = new HttpRequestMessage(HttpMethod.Post, url);
        message.Content = new StringContent(body, Encoding.UTF8, contentType);
        foreach (var (name, value) in headers)
        {
            if (!message.Headers.TryAddWithoutValidation(name, value))
            {
                message.Content.Headers.TryAddWithoutValidation(name, value);
            }
        }

        try
        {
            using var response = await client.SendAsync(message, cancellationToken);
            return new ConnectorHttpResult((int)response.StatusCode, null, null, null);
        }
        catch (Exception)
        {
            return new ConnectorHttpResult(0, null, null, "UpstreamFailed");
        }
    }

    private async Task UpsertAsync(
        SiemDelivery? existing,
        SiemDestination row,
        Guid eventId,
        string status,
        string? error,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        if (existing is null)
        {
            existing = new SiemDelivery
            {
                Id = Guid.NewGuid(),
                TenantId = row.TenantId,
                WorkspaceId = row.WorkspaceId,
                DestinationId = row.Id,
                EventId = eventId,
                CreatedAt = now
            };
            db.SiemDeliveries.Add(existing);
        }

        existing.Status = status;
        existing.Attempts += status is SiemDeliveryStatuses.Delivered or SiemDeliveryStatuses.Failed ? 1 : 0;
        existing.ErrorCode = error;
        if (status == SiemDeliveryStatuses.Delivered)
        {
            existing.DeliveredAt = now;
            existing.ErrorCode = null;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static void Advance(SiemDestination row, AuditEvent auditEvent)
    {
        row.CheckpointAt = auditEvent.OccurredAt;
        row.CheckpointEventId = auditEvent.Id;
    }

    private static void ApplyCircuit(SiemDestination row, SiemCircuitState state, string? error)
    {
        row.Circuit = state.Circuit;
        row.FailureCount = state.Failures;
        row.OpenUntil = state.OpenUntil;
        row.NextAttemptAt = state.NextAttemptAt;
        row.LastError = error is null ? null : error.Length <= 64 ? error : error[..64];
    }

    private string? Unprotect(SiemDestination row)
    {
        try
        {
            return secrets.Unprotect(
                row.SigningSecret,
                RuntimeSecretKinds.SiemSigningSecret,
                row.TenantId,
                row.WorkspaceId,
                row.Id.ToString("N"));
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private bool TryEgress(string? url, out Uri? uri, out string error)
    {
        uri = null;
        error = SiemErrors.UrlInvalid;
        if (string.IsNullOrWhiteSpace(url) || url.Length > SiemLimits.MaxUrlLength || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsed))
        {
            return false;
        }

        var first = lookup.Lookup(parsed.IdnHost);
        var second = lookup.Lookup(parsed.IdnHost);
        var decision = ConnectorPolicies.Authorize(parsed, first, second, [parsed.IdnHost]);
        if (!decision.Allowed)
        {
            error = SiemErrors.EgressBlocked;
            return false;
        }

        uri = parsed;
        error = string.Empty;
        return true;
    }

    private async Task<SiemDestination?> FindAsync(Workspace workspace, Guid destinationId, CancellationToken cancellationToken)
    {
        if (!Enabled())
        {
            return null;
        }

        return await db.SiemDestinations.FirstOrDefaultAsync(
            x => x.Id == destinationId && x.WorkspaceId == workspace.Id, cancellationToken);
    }

    private async Task<SiemCommandResult> MissingOrDisabled(Workspace workspace, Guid destinationId, CancellationToken cancellationToken)
    {
        if (!Enabled())
        {
            return SiemCommandResult.Fail(404, SiemErrors.Disabled);
        }

        return SiemCommandResult.Fail(404, SiemErrors.NotFound);
    }

    private async Task<object> ViewAsync(SiemDestination row, DateTimeOffset now, string? secret, CancellationToken cancellationToken)
    {
        DateTimeOffset? oldest = await LiveQuery(row).Select(x => (DateTimeOffset?)x.OccurredAt).FirstOrDefaultAsync(cancellationToken);
        return new
        {
            id = row.Id,
            name = row.Name,
            transport = row.Transport,
            url = row.Url,
            status = row.Status,
            circuit = row.Circuit,
            minSeverity = row.MinSeverity,
            categories = ReadCategories(row.CategoriesJson),
            minimize = row.Minimize,
            lastSuccessAt = row.LastSuccessAt,
            checkpointAt = row.CheckpointAt,
            lagSeconds = SiemRules.LagSeconds(oldest, now),
            secretMask = SecretMasking.MaskFromSuffix(row.SigningSecret.MaskSuffix),
            secret
        };
    }

    private void WriteAudit(Workspace workspace, UserId actor, string action, Guid destinationId, object metadata) =>
        audit.Add(new AuditEvent
        {
            TenantId = workspace.TenantId,
            ActorUserId = actor,
            Action = action,
            EntityType = "SiemDestination",
            EntityId = destinationId.ToString("D"),
            MetadataJson = JsonSerializer.Serialize(metadata, Json)
        });

    private static IQueryable<AuditEvent> WithCategories(IQueryable<AuditEvent> query, string[] categories)
    {
        if (categories.Length == 0)
        {
            return query;
        }

        var parameter = System.Linq.Expressions.Expression.Parameter(typeof(AuditEvent), "auditEvent");
        var action = System.Linq.Expressions.Expression.Property(parameter, nameof(AuditEvent.Action));
        var startsWith = typeof(string).GetMethod(nameof(string.StartsWith), [typeof(string)])!;
        System.Linq.Expressions.Expression? body = null;
        foreach (var category in categories)
        {
            var call = System.Linq.Expressions.Expression.Call(action, startsWith, System.Linq.Expressions.Expression.Constant(category + "."));
            body = body is null ? call : System.Linq.Expressions.Expression.OrElse(body, call);
        }

        var lambda = System.Linq.Expressions.Expression.Lambda<Func<AuditEvent, bool>>(body!, parameter);
        return query.Where(lambda);
    }

    private static string[] ReadCategories(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static bool TryName(string? name, out string trimmed)
    {
        trimmed = name?.Trim() ?? string.Empty;
        return trimmed.Length is > 0 and <= SiemLimits.MaxNameLength;
    }

    private static string NewSecret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    private bool Enabled() => options.Value.Enabled;
}
