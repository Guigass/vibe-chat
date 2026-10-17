using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VibeChat.Audit;
using VibeChat.BuildingBlocks;
using VibeChat.Conversations;
using VibeChat.Identity;
using VibeChat.Integrations;
using VibeChat.Messaging;
using VibeChat.SharedKernel;
using VibeChat.Tenancy;

namespace VibeChat.Infrastructure;

public sealed record ConnectorCall<T>(int Status, string? Error, T? Value);

public sealed record ConnectorInboundState(Guid EffectId, bool Duplicate);

public sealed record ConnectorDeliveryState(bool Ok, string? ErrorCode);

public sealed class ConnectorService(
    VibeChatDbContext db,
    ITenantContext tenantContext,
    IClock clock,
    RuntimeSecretProtector secrets,
    IMessageWriter messages,
    IAuditWriter audit,
    IConnectorTransport transport,
    IConnectorAddressLookup addresses)
{
    public Task<ConnectorCall<ConnectorInstallationState>> CreateAsync(
        TenantId tenantId,
        WorkspaceId workspaceId,
        UserId actorId,
        string idempotencyKey,
        string provider,
        string destinationKind,
        Guid destinationId,
        IReadOnlyCollection<string> scopes,
        string? token,
        string? clientId,
        string? baseUrl,
        CancellationToken cancellationToken) =>
        CreateCoreAsync(tenantId, workspaceId, actorId, idempotencyKey, provider, destinationKind, destinationId, scopes, token, clientId, baseUrl, cancellationToken);

    public async Task<IReadOnlyList<ConnectorInstallationState>> ListAsync(
        WorkspaceId workspaceId,
        CancellationToken cancellationToken)
    {
        var rows = await db.ConnectorInstallations.AsNoTracking()
            .Where(row => row.WorkspaceId == workspaceId)
            .OrderBy(row => row.CreatedAt)
            .ToListAsync(cancellationToken);
        return rows.Select(Map).ToArray();
    }

    public async Task<ConnectorCall<ConnectorInstallationState>> RotateAsync(
        WorkspaceId workspaceId,
        Guid installationId,
        UserId actorId,
        string token,
        CancellationToken cancellationToken)
    {
        if (!ConnectorPolicies.IsValidToken(token))
        {
            return Fail<ConnectorInstallationState>(400, "InvalidToken");
        }

        var row = await TrackAsync(workspaceId, installationId, cancellationToken);
        if (row is null)
        {
            return Fail<ConnectorInstallationState>(404, "ConnectorNotFound");
        }

        if (!string.Equals(row.Status, ConnectorPolicies.StatusActive, StringComparison.Ordinal))
        {
            return Fail<ConnectorInstallationState>(409, "ConnectorRevoked");
        }

        var now = clock.UtcNow;
        row.AccessToken = secrets.Protect(token, ConnectorPolicies.AccessKind, row.TenantId, row.WorkspaceId, row.Id.ToString("N"), now);
        row.SecretVersion += 1;
        row.UpdatedAt = now;
        row.Circuit = ConnectorPolicies.CircuitClosed;
        row.ConsecutiveFailures = 0;
        row.OpenUntil = null;
        row.NextAttemptAt = null;
        row.LastErrorCode = null;
        Audit(row, actorId, AuditActions.ConnectorRotated, now);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(Map(row));
    }

    public async Task<ConnectorCall<ConnectorInstallationState>> RevokeAsync(
        WorkspaceId workspaceId,
        Guid installationId,
        UserId actorId,
        CancellationToken cancellationToken)
    {
        var row = await TrackAsync(workspaceId, installationId, cancellationToken);
        if (row is null)
        {
            return Fail<ConnectorInstallationState>(404, "ConnectorNotFound");
        }

        var now = clock.UtcNow;
        if (!string.Equals(row.Status, ConnectorPolicies.StatusRevoked, StringComparison.Ordinal))
        {
            row.Status = ConnectorPolicies.StatusRevoked;
            row.RevokedAt = now;
            row.AccessToken.Clear();
            row.RefreshToken.Clear();
            row.OauthVerifier.Clear();
            row.OauthStateHash = null;
            row.Circuit = ConnectorPolicies.CircuitOpen;
            row.OpenUntil = null;
            row.UpdatedAt = now;
            Audit(row, actorId, AuditActions.ConnectorRevoked, now);
            VibeChatMetrics.ConnectorRevoked.Add(1);
            await db.SaveChangesAsync(cancellationToken);
        }

        return Ok(Map(row));
    }

    public async Task<ConnectorCall<ConnectorOauthStart>> StartOauthAsync(
        WorkspaceId workspaceId,
        Guid installationId,
        string? clientId,
        CancellationToken cancellationToken)
    {
        var row = await TrackAsync(workspaceId, installationId, cancellationToken);
        if (row is null)
        {
            return Fail<ConnectorOauthStart>(404, "ConnectorNotFound");
        }

        if (!string.Equals(row.Status, ConnectorPolicies.StatusActive, StringComparison.Ordinal))
        {
            return Fail<ConnectorOauthStart>(409, "ConnectorRevoked");
        }

        var entry = ConnectorPolicies.Find(row.Provider);
        if (entry is null || !entry.Oauth)
        {
            return Fail<ConnectorOauthStart>(400, "OauthNotSupported");
        }

        var id = ConnectorPolicies.Normalize(clientId ?? row.ClientId);
        if (!ConnectorPolicies.IsValidClientId(id))
        {
            return Fail<ConnectorOauthStart>(400, "ClientIdRequired");
        }

        var now = clock.UtcNow;
        var state = ConnectorPolicies.NewState();
        var (verifier, challenge) = ConnectorPolicies.NewPkce();
        var url = ConnectorPolicies.AuthorizeUrl(row.Provider, id, state, challenge);
        if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var authorize))
        {
            return Fail<ConnectorOauthStart>(400, "OauthNotSupported");
        }

        var decision = Authorize(authorize, entry.AllowlistHosts);
        if (!decision.Allowed)
        {
            return Fail<ConnectorOauthStart>(400, decision.ErrorCode ?? "EgressBlocked");
        }

        row.ClientId = id;
        row.OauthStateHash = ConnectorPolicies.HashState(state);
        row.OauthVerifier = secrets.Protect(verifier, ConnectorPolicies.VerifierKind, row.TenantId, row.WorkspaceId, row.Id.ToString("N"), now);
        row.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new ConnectorOauthStart(url, entry.Scopes));
    }

    public async Task<ConnectorCall<ConnectorInstallationState>> CompleteOauthAsync(
        WorkspaceId workspaceId,
        Guid installationId,
        string code,
        string state,
        CancellationToken cancellationToken)
    {
        var row = await TrackAsync(workspaceId, installationId, cancellationToken);
        if (row is null)
        {
            return Fail<ConnectorInstallationState>(404, "ConnectorNotFound");
        }

        if (!string.Equals(row.Status, ConnectorPolicies.StatusActive, StringComparison.Ordinal))
        {
            return Fail<ConnectorInstallationState>(409, "ConnectorRevoked");
        }

        var normalizedCode = ConnectorPolicies.Normalize(code);
        var normalizedState = ConnectorPolicies.Normalize(state);
        if (normalizedCode.Length is < 8 or > 200
            || row.OauthStateHash is null
            || !string.Equals(row.OauthStateHash, ConnectorPolicies.HashState(normalizedState), StringComparison.Ordinal)
            || !row.OauthVerifier.IsPresent
            || !ConnectorPolicies.IsValidClientId(row.ClientId))
        {
            return Fail<ConnectorInstallationState>(400, "OauthFailed");
        }

        var tokenUri = ConnectorPolicies.TokenUri(row.Provider);
        var entry = ConnectorPolicies.Find(row.Provider);
        if (tokenUri is null || entry is null)
        {
            return Fail<ConnectorInstallationState>(400, "OauthNotSupported");
        }

        var decision = Authorize(tokenUri, entry.AllowlistHosts);
        if (!decision.Allowed)
        {
            return Fail<ConnectorInstallationState>(400, decision.ErrorCode ?? "EgressBlocked");
        }

        string verifier;
        try
        {
            verifier = secrets.Unprotect(row.OauthVerifier, ConnectorPolicies.VerifierKind, row.TenantId, row.WorkspaceId, row.Id.ToString("N"));
        }
        catch (Exception)
        {
            return Fail<ConnectorInstallationState>(400, "OauthFailed");
        }

        var body = $"grant_type=authorization_code&code={Uri.EscapeDataString(normalizedCode)}&code_verifier={Uri.EscapeDataString(verifier)}&client_id={Uri.EscapeDataString(row.ClientId!)}";
        var result = await transport.SendAsync(
            new ConnectorHttpRequest("POST", tokenUri, body, "application/x-www-form-urlencoded", new Dictionary<string, string>
            {
                ["Accept"] = "application/json",
            }),
            cancellationToken);
        if (result.ErrorCode is not null || result.StatusCode is < 200 or >= 300)
        {
            return Fail<ConnectorInstallationState>(400, "OauthFailed");
        }

        if (!ConnectorPolicies.TryReadToken(result.Body, row.Provider, out var access, out var refresh, out var error))
        {
            return Fail<ConnectorInstallationState>(400, error);
        }

        var now = clock.UtcNow;
        row.AccessToken = secrets.Protect(access, ConnectorPolicies.AccessKind, row.TenantId, row.WorkspaceId, row.Id.ToString("N"), now);
        row.RefreshToken = refresh is null
            ? new EncryptedSecretEnvelope()
            : secrets.Protect(refresh, ConnectorPolicies.RefreshKind, row.TenantId, row.WorkspaceId, row.Id.ToString("N"), now);
        row.OauthVerifier.Clear();
        row.OauthStateHash = null;
        row.SecretVersion += 1;
        row.UpdatedAt = now;
        row.LastErrorCode = null;
        Audit(row, row.CreatedBy, AuditActions.ConnectorRotated, now);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(Map(row));
    }

    public Task<ConnectorCall<ConnectorDeliveryState>> ReconnectAsync(
        WorkspaceId workspaceId,
        Guid installationId,
        CancellationToken cancellationToken) =>
        DeliverAsync(workspaceId, installationId, clearBackoff: true, cancellationToken);

    public Task<ConnectorCall<ConnectorDeliveryState>> DeliverAsync(
        WorkspaceId workspaceId,
        Guid installationId,
        CancellationToken cancellationToken) =>
        DeliverAsync(workspaceId, installationId, clearBackoff: false, cancellationToken);

    public async Task<ConnectorCall<ConnectorInboundState>> AcceptInboundAsync(
        string opaqueKey,
        string body,
        string? ownSignature,
        string? githubSignature,
        string? gitlabToken,
        CancellationToken cancellationToken)
    {
        tenantContext.SetJobRole(ConnectorPolicies.JobRole);
        await RlsSession.EnsureAppliedAsync(db, tenantContext, cancellationToken);
        var row = await db.ConnectorInstallations.IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.OpaqueKey == opaqueKey, cancellationToken);
        tenantContext.SetJobRole(null);
        if (row is null)
        {
            return Fail<ConnectorInboundState>(404, "ConnectorDisabled");
        }

        if (!string.Equals(row.Status, ConnectorPolicies.StatusActive, StringComparison.Ordinal))
        {
            VibeChatMetrics.ConnectorRevoked.Add(1);
            return Fail<ConnectorInboundState>(410, "ConnectorRevoked");
        }

        tenantContext.SetTenant(row.TenantId);
        await RlsSession.EnsureAppliedAsync(db, tenantContext, cancellationToken);
        if (!row.AccessToken.IsPresent)
        {
            return Fail<ConnectorInboundState>(401, "ConnectorSignatureInvalid");
        }

        string secret;
        try
        {
            secret = secrets.Unprotect(row.AccessToken, ConnectorPolicies.AccessKind, row.TenantId, row.WorkspaceId, row.Id.ToString("N"));
        }
        catch (Exception)
        {
            return Fail<ConnectorInboundState>(401, "ConnectorSignatureInvalid");
        }

        if (!ConnectorPolicies.InboundAuthenticated(row.Provider, secret, body, ownSignature, githubSignature, gitlabToken))
        {
            return Fail<ConnectorInboundState>(401, "ConnectorSignatureInvalid");
        }

        if (!ConnectorPolicies.TryReadEvent(row.Provider, body, out var externalId, out var title, out _))
        {
            return Fail<ConnectorInboundState>(400, "InvalidConnectorEvent");
        }

        var existing = await db.ConnectorEffects
            .FirstOrDefaultAsync(item => item.InstallationId == row.Id && item.ExternalEventId == externalId, cancellationToken);
        if (existing is not null)
        {
            return Ok(new ConnectorInboundState(existing.Id, true));
        }

        var effectId = Guid.NewGuid();
        var effectRef = await ApplyEffectAsync(row, externalId, title, effectId, cancellationToken);
        var now = clock.UtcNow;
        db.ConnectorEffects.Add(new ConnectorEffect
        {
            Id = effectId,
            TenantId = row.TenantId,
            InstallationId = row.Id,
            ExternalEventId = externalId,
            DestinationKind = row.DestinationKind,
            DestinationId = row.DestinationId,
            EffectRef = effectRef,
            CreatedAt = now,
        });
        AddDelivery(row, ConnectorPolicies.DirectionInbound, ConnectorPolicies.DeliveryDelivered, null, 1, now);
        await db.SaveChangesAsync(cancellationToken);
        VibeChatMetrics.ConnectorInbound.Add(1);
        return Ok(new ConnectorInboundState(effectId, false));
    }

    private async Task<ConnectorCall<ConnectorInstallationState>> CreateCoreAsync(
        TenantId tenantId,
        WorkspaceId workspaceId,
        UserId actorId,
        string idempotencyKey,
        string provider,
        string destinationKind,
        Guid destinationId,
        IReadOnlyCollection<string> scopes,
        string? token,
        string? clientId,
        string? baseUrl,
        CancellationToken cancellationToken)
    {
        tenantContext.SetTenant(tenantId);
        await RlsSession.EnsureAppliedAsync(db, tenantContext, cancellationToken);
        var key = ConnectorPolicies.Normalize(idempotencyKey);
        var providerId = ConnectorPolicies.Normalize(provider).ToLowerInvariant();
        var kind = ConnectorPolicies.Normalize(destinationKind).ToLowerInvariant();
        if (!ConnectorPolicies.IsValidIdempotencyKey(key))
        {
            return Fail<ConnectorInstallationState>(400, "InvalidIdempotencyKey");
        }

        var existing = await db.ConnectorInstallations
            .FirstOrDefaultAsync(row => row.CreatedBy == actorId && row.IdempotencyKey == key, cancellationToken);
        if (existing is not null)
        {
            return Ok(Map(existing));
        }

        var entry = ConnectorPolicies.Find(providerId);
        if (entry is null)
        {
            return Fail<ConnectorInstallationState>(400, "UnknownConnector");
        }

        if (!ConnectorPolicies.IsDestination(kind))
        {
            return Fail<ConnectorInstallationState>(400, "InvalidDestination");
        }

        if (!ConnectorPolicies.ScopesMatch(scopes, entry.Scopes))
        {
            return Fail<ConnectorInstallationState>(400, "ScopeMismatch");
        }

        var normalizedToken = ConnectorPolicies.Normalize(token);
        var normalizedClient = ConnectorPolicies.Normalize(clientId);
        string? storedBase = null;
        string[] allowlist;
        if (string.Equals(providerId, ConnectorPolicies.ProviderCalendar, StringComparison.Ordinal))
        {
            if (!string.IsNullOrEmpty(normalizedToken) && !ConnectorPolicies.IsValidToken(normalizedToken))
            {
                return Fail<ConnectorInstallationState>(400, "InvalidToken");
            }

            if (!ConnectorPolicies.IsValidToken(normalizedToken))
            {
                return Fail<ConnectorInstallationState>(400, "TokenRequired");
            }

            if (!TryPinCalendar(baseUrl, out storedBase, out allowlist, out var pinError))
            {
                return Fail<ConnectorInstallationState>(400, pinError);
            }
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                return Fail<ConnectorInstallationState>(400, "UnexpectedBaseUrl");
            }

            if (!string.IsNullOrEmpty(normalizedToken) && !ConnectorPolicies.IsValidToken(normalizedToken))
            {
                return Fail<ConnectorInstallationState>(400, "InvalidToken");
            }

            if (!ConnectorPolicies.IsValidToken(normalizedToken) && !ConnectorPolicies.IsValidClientId(normalizedClient))
            {
                return Fail<ConnectorInstallationState>(400, "TokenRequired");
            }

            allowlist = entry.AllowlistHosts;
        }

        if (!await DestinationExistsAsync(workspaceId, kind, destinationId, cancellationToken))
        {
            return Fail<ConnectorInstallationState>(400, "DestinationNotFound");
        }

        var count = await db.ConnectorInstallations.CountAsync(row => row.WorkspaceId == workspaceId, cancellationToken);
        if (count >= ConnectorPolicies.MaxInstallationsPerWorkspace)
        {
            return Fail<ConnectorInstallationState>(409, "ConnectorLimit");
        }

        var now = clock.UtcNow;
        var id = Guid.NewGuid();
        var row = new ConnectorInstallation
        {
            Id = id,
            TenantId = tenantId,
            WorkspaceId = workspaceId,
            Provider = providerId,
            OpaqueKey = ConnectorPolicies.NewOpaqueKey(),
            Status = ConnectorPolicies.StatusActive,
            DestinationKind = kind,
            DestinationId = destinationId,
            Scopes = entry.Scopes.ToArray(),
            AllowlistHosts = allowlist,
            BaseUrl = storedBase,
            ClientId = ConnectorPolicies.IsValidClientId(normalizedClient) ? normalizedClient : null,
            SecretVersion = string.IsNullOrEmpty(normalizedToken) ? 0 : 1,
            Circuit = ConnectorPolicies.CircuitClosed,
            IdempotencyKey = key,
            CreatedBy = actorId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        if (ConnectorPolicies.IsValidToken(normalizedToken))
        {
            row.AccessToken = secrets.Protect(normalizedToken, ConnectorPolicies.AccessKind, tenantId, workspaceId, id.ToString("N"), now);
        }

        db.ConnectorInstallations.Add(row);
        db.ConnectorCheckpoints.Add(new ConnectorCheckpoint
        {
            InstallationId = id,
            TenantId = tenantId,
            Cursor = string.Empty,
            UpdatedAt = now,
        });
        Audit(row, actorId, AuditActions.ConnectorInstalled, now);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(Map(row));
    }

    private async Task<ConnectorCall<ConnectorDeliveryState>> DeliverAsync(
        WorkspaceId workspaceId,
        Guid installationId,
        bool clearBackoff,
        CancellationToken cancellationToken)
    {
        var row = await TrackAsync(workspaceId, installationId, cancellationToken);
        if (row is null)
        {
            return Fail<ConnectorDeliveryState>(404, "ConnectorNotFound");
        }

        if (clearBackoff && string.Equals(row.Status, ConnectorPolicies.StatusActive, StringComparison.Ordinal))
        {
            row.NextAttemptAt = null;
        }

        var now = clock.UtcNow;
        if (!ConnectorPolicies.MayCall(row, now, out var blocked))
        {
            AddDelivery(row, ConnectorPolicies.DirectionOutbound, ConnectorPolicies.DeliverySkipped, blocked, row.ConsecutiveFailures, now);
            await db.SaveChangesAsync(cancellationToken);
            return Ok(new ConnectorDeliveryState(false, blocked));
        }

        var probe = ConnectorPolicies.ProbeUri(row);
        if (probe is null)
        {
            return await FailDeliveryAsync(row, "EgressBlocked", now, called: false, cancellationToken);
        }

        var decision = Authorize(probe, row.AllowlistHosts);
        if (!decision.Allowed)
        {
            return await FailDeliveryAsync(row, decision.ErrorCode ?? "EgressBlocked", now, called: false, cancellationToken);
        }

        var headers = new Dictionary<string, string>(StringComparer.Ordinal);
        if (row.AccessToken.IsPresent)
        {
            try
            {
                var token = secrets.Unprotect(row.AccessToken, ConnectorPolicies.AccessKind, row.TenantId, row.WorkspaceId, row.Id.ToString("N"));
                headers["Authorization"] = "Bearer " + token;
            }
            catch (Exception)
            {
                return await FailDeliveryAsync(row, "UpstreamFailed", now, called: false, cancellationToken);
            }
        }

        var result = await transport.SendAsync(new ConnectorHttpRequest("GET", probe, null, null, headers), cancellationToken);
        if (result.StatusCode is >= 300 and < 400)
        {
            var next = Resolve(probe, result.Location);
            if (next is null)
            {
                return await FailDeliveryAsync(row, "EgressBlocked", now, called: true, cancellationToken);
            }

            var redirect = Authorize(next, row.AllowlistHosts);
            if (!redirect.Allowed)
            {
                return await FailDeliveryAsync(row, redirect.ErrorCode ?? "EgressBlocked", now, called: true, cancellationToken);
            }

            result = await transport.SendAsync(new ConnectorHttpRequest("GET", next, null, null, headers), cancellationToken);
        }

        if (result.ErrorCode is not null || result.StatusCode is < 200 or >= 300)
        {
            VibeChatMetrics.ConnectorOutboundFailed.Add(1);
            return await FailDeliveryAsync(row, result.ErrorCode ?? "UpstreamFailed", now, called: true, cancellationToken);
        }

        var success = ConnectorPolicies.RecordSuccess();
        row.Circuit = success.Circuit;
        row.ConsecutiveFailures = success.Failures;
        row.OpenUntil = success.OpenUntil;
        row.NextAttemptAt = success.NextAttemptAt;
        row.LastErrorCode = null;
        row.UpdatedAt = now;
        var checkpoint = await db.ConnectorCheckpoints.FirstOrDefaultAsync(item => item.InstallationId == row.Id, cancellationToken);
        if (checkpoint is null)
        {
            db.ConnectorCheckpoints.Add(new ConnectorCheckpoint
            {
                InstallationId = row.Id,
                TenantId = row.TenantId,
                Cursor = now.ToString("O"),
                UpdatedAt = now,
            });
        }
        else
        {
            checkpoint.Cursor = now.ToString("O");
            checkpoint.UpdatedAt = now;
        }

        AddDelivery(row, ConnectorPolicies.DirectionOutbound, ConnectorPolicies.DeliveryDelivered, null, 1, now);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new ConnectorDeliveryState(true, null));
    }

    private async Task<ConnectorCall<ConnectorDeliveryState>> FailDeliveryAsync(
        ConnectorInstallation row,
        string error,
        DateTimeOffset now,
        bool called,
        CancellationToken cancellationToken)
    {
        if (called || error is "EgressBlocked" or "DnsRebinding")
        {
            var snapshot = ConnectorPolicies.RecordFailure(row.ConsecutiveFailures, now);
            row.Circuit = snapshot.Circuit;
            row.ConsecutiveFailures = snapshot.Failures;
            row.OpenUntil = snapshot.OpenUntil;
            row.NextAttemptAt = snapshot.NextAttemptAt;
        }

        row.LastErrorCode = error;
        row.UpdatedAt = now;
        AddDelivery(row, ConnectorPolicies.DirectionOutbound, ConnectorPolicies.DeliveryFailed, error, row.ConsecutiveFailures, now);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new ConnectorDeliveryState(false, error));
    }

    private async Task<Guid> ApplyEffectAsync(
        ConnectorInstallation row,
        string externalId,
        string title,
        Guid effectId,
        CancellationToken cancellationToken)
    {
        var summary = ConnectorPolicies.Summary(row.Provider, externalId, title);
        if (string.Equals(row.DestinationKind, ConnectorPolicies.DestinationChannel, StringComparison.Ordinal))
        {
            var identity = await EnsureIdentityAsync(row, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            var sent = await messages.SendAsync(new SendMessageCommand(
                row.TenantId,
                identity,
                new ChannelId(row.DestinationId),
                MessageId.New(),
                $"cx-{row.Id:N}-{externalId}",
                summary,
                null,
                null,
                null,
                AuthorIsBot: true), cancellationToken);
            return sent.MessageId.Value;
        }

        if (string.Equals(row.DestinationKind, ConnectorPolicies.DestinationTask, StringComparison.Ordinal))
        {
            var task = await db.MessageTasks.FirstOrDefaultAsync(
                item => item.Id == row.DestinationId && item.WorkspaceId == row.WorkspaceId, cancellationToken)
                ?? throw new InvalidOperationException("DestinationNotFound");
            var text = summary.Length <= TaskPolicies.MaxChecklistTextLength
                ? summary
                : summary[..TaskPolicies.MaxChecklistTextLength];
            var position = await db.MessageTaskChecklistItems.CountAsync(item => item.TaskId == task.Id, cancellationToken);
            var itemId = Guid.NewGuid();
            db.MessageTaskChecklistItems.Add(new MessageTaskChecklistItem
            {
                Id = itemId,
                TenantId = row.TenantId,
                TaskId = task.Id,
                Position = position + 1,
                Text = text,
                Done = false,
            });
            return itemId;
        }

        var incident = await db.IncidentRooms.FirstOrDefaultAsync(
            item => item.Id == row.DestinationId && item.WorkspaceId == row.WorkspaceId, cancellationToken)
            ?? throw new InvalidOperationException("DestinationNotFound");
        if (string.Equals(incident.Status, IncidentPolicies.Resolved, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("IncidentClosed");
        }

        db.IncidentTimeline.Add(new IncidentTimelineEntry
        {
            Id = Guid.NewGuid(),
            TenantId = row.TenantId,
            IncidentId = incident.Id,
            Kind = "connector",
            SourceId = effectId,
            IdempotencyKey = externalId,
            CreatedAt = clock.UtcNow,
        });
        return effectId;
    }

    private async Task<UserId> EnsureIdentityAsync(ConnectorInstallation row, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        if (row.IdentityUserId is null)
        {
            var userId = UserId.New();
            row.IdentityUserId = userId;
            db.UserProfiles.Add(new UserProfile
            {
                Id = userId,
                Subject = $"{ConnectorPolicies.IdentitySubjectPrefix}{row.Id:N}",
                Email = $"connector+{row.Id:N}@connectors.vibechat.local",
                DisplayName = row.Provider,
                CreatedAt = now,
                UpdatedAt = now,
            });
            db.WorkspaceMembers.Add(new WorkspaceMember
            {
                Id = Guid.NewGuid(),
                TenantId = row.TenantId,
                WorkspaceId = row.WorkspaceId,
                UserId = userId,
                Role = Role.Bot,
                JoinedAt = now,
            });
        }

        var identity = row.IdentityUserId.Value;
        var channel = await db.Channels.FirstOrDefaultAsync(
            item => item.Id == new ChannelId(row.DestinationId) && item.WorkspaceId == row.WorkspaceId, cancellationToken);
        if (channel is not null && channel.Type.RequiresChannelMembership())
        {
            var member = await db.ChannelMembers.FirstOrDefaultAsync(
                item => item.ChannelId == channel.Id && item.UserId == identity, cancellationToken);
            if (member is null)
            {
                db.ChannelMembers.Add(new ChannelMember
                {
                    Id = Guid.NewGuid(),
                    TenantId = row.TenantId,
                    ChannelId = channel.Id,
                    UserId = identity,
                    JoinedAt = now,
                });
            }
        }

        return identity;
    }

    private async Task<bool> DestinationExistsAsync(
        WorkspaceId workspaceId,
        string kind,
        Guid destinationId,
        CancellationToken cancellationToken)
    {
        if (string.Equals(kind, ConnectorPolicies.DestinationChannel, StringComparison.Ordinal))
        {
            return await db.Channels.AnyAsync(
                row => row.Id == new ChannelId(destinationId) && row.WorkspaceId == workspaceId, cancellationToken);
        }

        if (string.Equals(kind, ConnectorPolicies.DestinationTask, StringComparison.Ordinal))
        {
            return await db.MessageTasks.AnyAsync(
                row => row.Id == destinationId && row.WorkspaceId == workspaceId, cancellationToken);
        }

        return await db.IncidentRooms.AnyAsync(
            row => row.Id == destinationId && row.WorkspaceId == workspaceId, cancellationToken);
    }

    private bool TryPinCalendar(string? baseUrl, out string? stored, out string[] allowlist, out string error)
    {
        stored = null;
        allowlist = [];
        error = "EgressBlocked";
        if (!Uri.TryCreate(ConnectorPolicies.Normalize(baseUrl), UriKind.Absolute, out var uri)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        var host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
        var decision = Authorize(uri, [host]);
        if (!decision.Allowed)
        {
            error = decision.ErrorCode ?? "EgressBlocked";
            return false;
        }

        stored = uri.GetComponents(UriComponents.Scheme | UriComponents.Host | UriComponents.Port | UriComponents.Path, UriFormat.UriEscaped);
        allowlist = [host];
        error = string.Empty;
        return true;
    }

    private EgressDecision Authorize(Uri url, IReadOnlyCollection<string> allowlist)
    {
        var first = ConnectorPolicies.AddressesFor(url, addresses);
        var second = ConnectorPolicies.AddressesFor(url, addresses);
        return ConnectorPolicies.Authorize(url, first, second, allowlist);
    }

    private async Task<ConnectorInstallation?> TrackAsync(WorkspaceId workspaceId, Guid installationId, CancellationToken cancellationToken)
    {
        await RlsSession.EnsureAppliedAsync(db, tenantContext, cancellationToken);
        return await db.ConnectorInstallations.FirstOrDefaultAsync(
            row => row.Id == installationId && row.WorkspaceId == workspaceId, cancellationToken);
    }

    private void AddDelivery(
        ConnectorInstallation row,
        string direction,
        string status,
        string? error,
        int attempt,
        DateTimeOffset now)
    {
        db.ConnectorDeliveries.Add(new ConnectorDelivery
        {
            Id = Guid.NewGuid(),
            TenantId = row.TenantId,
            InstallationId = row.Id,
            Direction = direction,
            Status = status,
            ErrorCode = error,
            Attempt = attempt,
            CreatedAt = now,
        });
    }

    private void Audit(ConnectorInstallation row, UserId actor, string action, DateTimeOffset now)
    {
        audit.Add(new AuditEvent
        {
            Id = Guid.NewGuid(),
            TenantId = row.TenantId,
            ActorUserId = actor,
            Action = action,
            EntityType = "Connector",
            EntityId = row.Id.ToString(),
            MetadataJson = JsonSerializer.Serialize(new { provider = row.Provider, status = row.Status }),
            OccurredAt = now,
        });
    }

    private static Uri? Resolve(Uri current, string? location)
    {
        if (string.IsNullOrWhiteSpace(location) || !Uri.TryCreate(location, UriKind.RelativeOrAbsolute, out var next))
        {
            return null;
        }

        return next.IsAbsoluteUri ? next : new Uri(current, next);
    }

    private static ConnectorInstallationState Map(ConnectorInstallation row) =>
        new(
            row.Id,
            row.Provider,
            row.Status,
            row.DestinationKind,
            row.DestinationId,
            row.Scopes,
            SecretMasking.MaskFromSuffix(row.AccessToken.MaskSuffix),
            row.Circuit,
            row.LastErrorCode,
            row.SecretVersion,
            ConnectorPolicies.InboundPath(row.OpaqueKey));

    private static ConnectorCall<T> Ok<T>(T value) => new(200, null, value);

    private static ConnectorCall<T> Fail<T>(int status, string error) => new(status, error, default);
}
