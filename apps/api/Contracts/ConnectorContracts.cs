namespace VibeChat.Api.Contracts;

public sealed record CreateConnectorRequest(
    string IdempotencyKey,
    string Provider,
    string DestinationKind,
    Guid DestinationId,
    string[] Scopes,
    string? Token,
    string? ClientId,
    string? BaseUrl);

public sealed record RotateConnectorRequest(string Token);

public sealed record ConnectorOauthCallbackRequest(string Code, string State);

public sealed record ConnectorInstallationResponse(
    Guid Id,
    string Provider,
    string Status,
    string DestinationKind,
    Guid DestinationId,
    string[] Scopes,
    string? SecretMask,
    string Circuit,
    string? LastErrorCode,
    int SecretVersion,
    string InboundPath);

public sealed record ConnectorCatalogResponse(string Id, string[] Scopes, bool Oauth);
