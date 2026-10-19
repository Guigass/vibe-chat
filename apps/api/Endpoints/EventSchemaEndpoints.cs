using System.Text.Json;
using Microsoft.Extensions.Options;
using VibeChat.Infrastructure;
using VibeChat.Integrations;
using VibeChat.SharedKernel;

namespace VibeChat.Api.Endpoints;

internal static class EventSchemaEndpoints
{
    internal static void MapEventSchemas(this RouteGroupBuilder v1)
    {
        v1.MapGet("/admin/event-schemas", List)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapGet("/admin/event-schemas/{eventType}/versions/{schemaVersion:int}", GetVersion)
            .RequirePermission(Permissions.Workspace.Admin);
        v1.MapPost("/admin/event-schemas/check", Check)
            .RequirePermission(Permissions.Workspace.Admin);
    }

    private static IResult List(IOptions<EventSchemaOptions> options)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        return Results.Ok(PublicEventCatalog.Versions.Select(Describe));
    }

    private static IResult GetVersion(string eventType, int schemaVersion, IOptions<EventSchemaOptions> options)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        var version = PublicEventCatalog.Versions.FirstOrDefault(item =>
            string.Equals(item.EventType, eventType, StringComparison.Ordinal)
            && item.SchemaVersion == schemaVersion);
        if (version is null)
        {
            var known = PublicEventCatalog.Versions.Any(item =>
                string.Equals(item.EventType, eventType, StringComparison.Ordinal));
            var reason = known
                ? EventSchemaReasons.UnknownSchemaVersion
                : EventSchemaReasons.UnknownEventType;
            Observe(reason, known ? eventType : "unknown");
            return Results.NotFound(new { error = reason });
        }

        return Results.Ok(Describe(version));
    }

    private static IResult Check(
        CheckEventSchemaRequest request,
        HttpContext http,
        IOptions<EventSchemaOptions> options)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        if (http.Request.ContentLength > PublicEventCatalog.MaxDocumentBytes)
        {
            Observe(EventSchemaReasons.SchemaDocumentTooLarge, "unknown");
            return Results.BadRequest(new { error = EventSchemaReasons.SchemaDocumentTooLarge });
        }

        var document = request.Document.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? "null"
            : request.Document.GetRawText();
        var decision = PublicEventCatalog.Validate(request.EventType, request.SchemaVersion, document);
        if (!decision.Accepted)
        {
            Observe(decision.Reason, decision.EventType);
            return decision.Reason switch
            {
                EventSchemaReasons.SchemaDocumentTooLarge => Results.BadRequest(new { error = decision.Reason }),
                EventSchemaReasons.SchemaMismatch => Results.UnprocessableEntity(new { error = decision.Reason }),
                _ => Results.NotFound(new { error = decision.Reason })
            };
        }

        return Results.Ok(new
        {
            accepted = true,
            eventType = decision.EventType,
            schemaVersion = decision.SchemaVersion
        });
    }

    private static void Observe(string reason, string eventType) =>
        VibeChatMetrics.EventSchemaRejected.Add(
            1,
            new KeyValuePair<string, object?>("reason", reason),
            new KeyValuePair<string, object?>("event_type", eventType));

    private static object Describe(PublicEventVersion version) => new
    {
        eventType = version.EventType,
        schemaVersion = version.SchemaVersion,
        wireName = version.WireName,
        status = version.Status == PublicEventStatus.Deprecated ? "deprecated" : "active",
        deprecatedUntil = version.DeprecatedUntil?.ToString("yyyy-MM-dd"),
        changelog = version.Changelog,
        schema = JsonSerializer.Deserialize<JsonElement>(version.Schema.ToJsonString()),
        example = JsonSerializer.Deserialize<JsonElement>(version.Example.ToJsonString())
    };

    private static IResult Disabled() =>
        Results.NotFound(new { error = EventSchemaReasons.Disabled });

    private sealed record CheckEventSchemaRequest(string? EventType, int SchemaVersion, JsonElement Document);
}
