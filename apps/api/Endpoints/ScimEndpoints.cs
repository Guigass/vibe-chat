using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VibeChat.BuildingBlocks;
using VibeChat.Identity;
using VibeChat.Infrastructure;
using VibeChat.SharedKernel;

namespace VibeChat.Api.Endpoints;

internal static class ScimEndpoints
{
    internal static void MapScim(this WebApplication app)
    {
        var scim = app.MapGroup("/scim/v2");
        scim.MapGet("/ServiceProviderConfig", Config);
        scim.MapGet("/Schemas", Schemas);
        scim.MapGet("/ResourceTypes", ResourceTypes);
        scim.MapGet("/Users", ListUsers);
        scim.MapPost("/Users", CreateUser);
        scim.MapGet("/Users/{id:guid}", GetUser);
        scim.MapPut("/Users/{id:guid}", ReplaceUser);
        scim.MapPatch("/Users/{id:guid}", PatchUser);
        scim.MapDelete("/Users/{id:guid}", DeleteUser);
        scim.MapGet("/Groups", ListGroups);
        scim.MapPost("/Groups", CreateGroup);
        scim.MapGet("/Groups/{id:guid}", GetGroup);
        scim.MapPut("/Groups/{id:guid}", ReplaceGroup);
        scim.MapPatch("/Groups/{id:guid}", PatchGroup);
        scim.MapDelete("/Groups/{id:guid}", DeleteGroup);
        scim.MapPost("/Bulk", Bulk);
    }

    private static Task<IResult> Config(HttpContext http, VibeChatDbContext db, ITenantContext tenant, IOptions<ScimOptions> options, IClock clock, CancellationToken ct) =>
        WithToken(http, db, tenant, options, clock, "users", ct, _ => Task.FromResult(Results.Json(new JsonObject
        {
            ["schemas"] = Arr(ScimSchemas.ServiceProviderConfig),
            ["patch"] = new JsonObject { ["supported"] = true },
            ["bulk"] = new JsonObject { ["supported"] = true, ["maxOperations"] = ScimPolicies.BulkMax, ["maxPayloadSize"] = 1048576 },
            ["filter"] = new JsonObject { ["supported"] = true, ["maxResults"] = ScimPolicies.PageMax },
            ["changePassword"] = new JsonObject { ["supported"] = false },
            ["sort"] = new JsonObject { ["supported"] = false },
            ["etag"] = new JsonObject { ["supported"] = false }
        })));

    private static Task<IResult> Schemas(HttpContext http, VibeChatDbContext db, ITenantContext tenant, IOptions<ScimOptions> options, IClock clock, CancellationToken ct) =>
        WithToken(http, db, tenant, options, clock, "users", ct, _ => Task.FromResult(Results.Json(new JsonObject
        {
            ["schemas"] = Arr(ScimSchemas.List),
            ["totalResults"] = 2,
            ["Resources"] = new JsonArray(
                new JsonObject { ["id"] = ScimSchemas.User, ["name"] = "User" },
                new JsonObject { ["id"] = ScimSchemas.Group, ["name"] = "Group" })
        })));

    private static Task<IResult> ResourceTypes(HttpContext http, VibeChatDbContext db, ITenantContext tenant, IOptions<ScimOptions> options, IClock clock, CancellationToken ct) =>
        WithToken(http, db, tenant, options, clock, "users", ct, _ => Task.FromResult(Results.Json(new JsonObject
        {
            ["schemas"] = Arr(ScimSchemas.List),
            ["totalResults"] = 2,
            ["Resources"] = new JsonArray(
                new JsonObject { ["id"] = "User", ["endpoint"] = "/Users", ["schema"] = ScimSchemas.User },
                new JsonObject { ["id"] = "Group", ["endpoint"] = "/Groups", ["schema"] = ScimSchemas.Group })
        })));

    private static Task<IResult> ListUsers(HttpContext http, VibeChatDbContext db, ITenantContext tenant, IOptions<ScimOptions> options, IClock clock, CancellationToken ct) =>
        WithToken(http, db, tenant, options, clock, "users", ct, async token =>
        {
            if (!ScimFilter.TryParse(http.Request.Query["filter"], out var field, out var value, out var error))
            {
                return Error(400, error, "InvalidFilter");
            }

            var query = db.ScimPrincipals.AsNoTracking().Where(x => x.TenantId == token.TenantId);
            if (!string.IsNullOrWhiteSpace(http.Request.Query["filter"]))
            {
                query = field switch
                {
                    ScimFilterField.ExternalId => query.Where(x => x.ExternalId == value),
                    ScimFilterField.DisplayName => query.Where(x => x.DisplayName == value),
                    _ => query.Where(x => x.UserName == (ScimPolicies.NormalizeUserName(value) ?? value))
                };
            }

            return await Page(http, query.OrderBy(x => x.Id), UserJson, ct);
        });

    private static Task<IResult> GetUser(Guid id, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IOptions<ScimOptions> options, IClock clock, CancellationToken ct) =>
        WithToken(http, db, tenant, options, clock, "users", ct, async _ =>
        {
            var row = await db.ScimPrincipals.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return row is null ? Error(404, null, "PrincipalNotFound") : Results.Json(UserJson(row));
        });

    private static Task<IResult> CreateUser(JsonElement body, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IProvisioningPort port, IOptions<ScimOptions> options, IClock clock, CancellationToken ct) =>
        WithToken(http, db, tenant, options, clock, "users", ct, async token =>
        {
            var result = await port.ApplyUserAsync(UserCommand(token, null, body, active: ReadBool(body, "active") ?? true), ct);
            return UserResult(result, created: result.Created);
        });

    private static Task<IResult> ReplaceUser(Guid id, JsonElement body, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IProvisioningPort port, IOptions<ScimOptions> options, IClock clock, CancellationToken ct) =>
        WithToken(http, db, tenant, options, clock, "users", ct, async token =>
        {
            var result = await port.ApplyUserAsync(UserCommand(token, id, body, active: ReadBool(body, "active") ?? true), ct);
            return UserResult(result, created: false);
        });

    private static Task<IResult> PatchUser(Guid id, JsonElement body, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IProvisioningPort port, IOptions<ScimOptions> options, IClock clock, CancellationToken ct) =>
        WithToken(http, db, tenant, options, clock, "users", ct, async token =>
        {
            var current = await db.ScimPrincipals.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (current is null)
            {
                return Error(404, null, "PrincipalNotFound");
            }

            if (!TryApplyPatch(body, current.UserName, current.DisplayName, current.ExternalId, current.Active,
                    out var userName, out var display, out var externalId, out var active, out var error))
            {
                return Error(400, "invalidSyntax", error);
            }

            var result = await port.ApplyUserAsync(new ProvisionUserCommand(
                token.TenantId, id, externalId, userName, display, active, "scim", token.CreatedByUserId), ct);
            return UserResult(result, created: false);
        });

    private static Task<IResult> DeleteUser(Guid id, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IProvisioningPort port, IOptions<ScimOptions> options, IClock clock, CancellationToken ct) =>
        WithToken(http, db, tenant, options, clock, "users", ct, async token =>
        {
            var current = await db.ScimPrincipals.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (current is null)
            {
                return Error(404, null, "PrincipalNotFound");
            }

            var result = await port.ApplyUserAsync(new ProvisionUserCommand(
                token.TenantId, id, current.ExternalId, current.UserName, current.DisplayName, false, "scim", token.CreatedByUserId), ct);
            return result.Ok ? Results.NoContent() : UserResult(result, created: false);
        });

    private static Task<IResult> ListGroups(HttpContext http, VibeChatDbContext db, ITenantContext tenant, IOptions<ScimOptions> options, IClock clock, CancellationToken ct) =>
        WithToken(http, db, tenant, options, clock, "groups", ct, async token =>
        {
            if (!ScimFilter.TryParse(http.Request.Query["filter"], out var field, out var value, out var error))
            {
                return Error(400, error, "InvalidFilter");
            }

            var query = db.ScimGroups.AsNoTracking().Where(x => x.TenantId == token.TenantId);
            if (!string.IsNullOrWhiteSpace(http.Request.Query["filter"]))
            {
                query = field switch
                {
                    ScimFilterField.ExternalId => query.Where(x => x.ExternalId == value),
                    _ => query.Where(x => x.DisplayName == value)
                };
            }

            return await Page(http, query.OrderBy(x => x.Id), group => GroupJson(group, []), ct);
        });

    private static Task<IResult> GetGroup(Guid id, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IOptions<ScimOptions> options, IClock clock, CancellationToken ct) =>
        WithToken(http, db, tenant, options, clock, "groups", ct, async _ =>
        {
            var row = await db.ScimGroups.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (row is null)
            {
                return Error(404, null, "GroupNotFound");
            }

            var members = await db.ScimGroupMembers.AsNoTracking().Where(x => x.GroupId == id).Select(x => x.PrincipalId).ToListAsync(ct);
            return Results.Json(GroupJson(row, members));
        });

    private static Task<IResult> CreateGroup(JsonElement body, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IProvisioningPort port, IOptions<ScimOptions> options, IClock clock, CancellationToken ct) =>
        WithToken(http, db, tenant, options, clock, "groups", ct, async token =>
        {
            var result = await port.ApplyGroupAsync(GroupCommand(token, null, body, remove: false), ct);
            return GroupResult(result, created: result.Created);
        });

    private static Task<IResult> ReplaceGroup(Guid id, JsonElement body, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IProvisioningPort port, IOptions<ScimOptions> options, IClock clock, CancellationToken ct) =>
        WithToken(http, db, tenant, options, clock, "groups", ct, async token =>
        {
            var result = await port.ApplyGroupAsync(GroupCommand(token, id, body, remove: false), ct);
            return GroupResult(result, created: false);
        });

    private static Task<IResult> PatchGroup(Guid id, JsonElement body, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IProvisioningPort port, IOptions<ScimOptions> options, IClock clock, CancellationToken ct) =>
        WithToken(http, db, tenant, options, clock, "groups", ct, async token =>
        {
            var current = await db.ScimGroups.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (current is null)
            {
                return Error(404, null, "GroupNotFound");
            }

            var display = current.DisplayName;
            var external = current.ExternalId;
            IReadOnlyList<Guid>? members = null;
            if (TryGet(body, "Operations", out var ops) && ops.ValueKind == JsonValueKind.Array)
            {
                foreach (var op in ops.EnumerateArray())
                {
                    if (!string.Equals(ReadString(op, "op"), "replace", StringComparison.OrdinalIgnoreCase))
                    {
                        return Error(400, "invalidSyntax", "UnsupportedPatch");
                    }

                    var path = ReadString(op, "path") ?? "";
                    if (!TryGet(op, "value", out var value))
                    {
                        return Error(400, "invalidSyntax", "UnsupportedPatch");
                    }

                    if (path is "displayName")
                    {
                        display = value.GetString();
                    }
                    else if (path is "externalId")
                    {
                        external = value.GetString();
                    }
                    else if (path is "members")
                    {
                        members = ReadMembers(value);
                    }
                    else
                    {
                        return Error(400, "invalidPath", "UnsupportedPatch");
                    }
                }
            }

            var result = await port.ApplyGroupAsync(new ProvisionGroupCommand(
                token.TenantId, id, external, display, members, false, "scim", token.CreatedByUserId), ct);
            return GroupResult(result, created: false);
        });

    private static Task<IResult> DeleteGroup(Guid id, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IProvisioningPort port, IOptions<ScimOptions> options, IClock clock, CancellationToken ct) =>
        WithToken(http, db, tenant, options, clock, "groups", ct, async token =>
        {
            var result = await port.ApplyGroupAsync(new ProvisionGroupCommand(
                token.TenantId, id, null, null, null, true, "scim", token.CreatedByUserId), ct);
            return result.Ok ? Results.NoContent() : GroupResult(result, created: false);
        });

    private static Task<IResult> Bulk(JsonElement body, HttpContext http, VibeChatDbContext db, ITenantContext tenant, IProvisioningPort port, IOptions<ScimOptions> options, IClock clock, CancellationToken ct) =>
        WithToken(http, db, tenant, options, clock, "users", ct, async token =>
        {
            if (!TryGet(body, "Operations", out var ops) || ops.ValueKind != JsonValueKind.Array)
            {
                return Error(400, "invalidSyntax", "InvalidBulk");
            }

            if (ops.GetArrayLength() > ScimPolicies.BulkMax)
            {
                return Error(413, "tooMany", "BulkTooLarge");
            }

            var response = new JsonArray();
            foreach (var op in ops.EnumerateArray())
            {
                var method = (ReadString(op, "method") ?? "").ToUpperInvariant();
                var path = ReadString(op, "path") ?? "";
                var bulkId = ReadString(op, "bulkId");
                TryGet(op, "data", out var data);
                var one = await RunBulkAsync(token, port, db, method, path, data, ct);
                response.Add(new JsonObject
                {
                    ["method"] = method,
                    ["bulkId"] = bulkId,
                    ["status"] = one.Status.ToString(),
                    ["location"] = one.Location,
                    ["response"] = one.Body
                });
            }

            return Results.Json(new JsonObject
            {
                ["schemas"] = Arr(ScimSchemas.BulkResponse),
                ["Operations"] = response
            });
        });

    private static async Task<BulkItem> RunBulkAsync(
        ScimToken token,
        IProvisioningPort port,
        VibeChatDbContext db,
        string method,
        string path,
        JsonElement data,
        CancellationToken ct)
    {
        var users = path.StartsWith("/Users", StringComparison.OrdinalIgnoreCase);
        var groups = path.StartsWith("/Groups", StringComparison.OrdinalIgnoreCase);
        if (!users && !groups)
        {
            return new BulkItem(400, null, ErrorNode(400, "invalidPath", "UnsupportedBulk"));
        }

        if (!token.Scopes.Contains(users ? "users" : "groups", StringComparison.Ordinal))
        {
            return new BulkItem(403, null, ErrorNode(403, null, "InsufficientScope"));
        }

        Guid? id = path.Length > 7 && Guid.TryParse(path[(path.LastIndexOf('/') + 1)..], out var parsed) ? parsed : null;
        if (method == "POST" && users)
        {
            var result = await port.ApplyUserAsync(UserCommand(token, null, data, ReadBool(data, "active") ?? true), ct);
            return result.Ok
                ? new BulkItem(result.Created ? 201 : 200, $"/scim/v2/Users/{result.PrincipalId}", UserNode(result))
                : new BulkItem(StatusFor(result.Error), null, ErrorNode(StatusFor(result.Error), null, result.Error));
        }

        if (method == "DELETE" && users && id is { } userId)
        {
            var current = await db.ScimPrincipals.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId, ct);
            if (current is null)
            {
                return new BulkItem(404, null, ErrorNode(404, null, "PrincipalNotFound"));
            }

            var result = await port.ApplyUserAsync(new ProvisionUserCommand(
                token.TenantId, userId, current.ExternalId, current.UserName, current.DisplayName, false, "scim", token.CreatedByUserId), ct);
            return result.Ok
                ? new BulkItem(204, null, null)
                : new BulkItem(StatusFor(result.Error), null, ErrorNode(StatusFor(result.Error), null, result.Error));
        }

        if (method == "POST" && groups)
        {
            var result = await port.ApplyGroupAsync(GroupCommand(token, null, data, false), ct);
            return result.Ok
                ? new BulkItem(result.Created ? 201 : 200, $"/scim/v2/Groups/{result.GroupId}", GroupNode(result))
                : new BulkItem(StatusFor(result.Error), null, ErrorNode(StatusFor(result.Error), null, result.Error));
        }

        return new BulkItem(400, null, ErrorNode(400, "invalidPath", "UnsupportedBulk"));
    }

    private static async Task<IResult> WithToken(
        HttpContext http,
        VibeChatDbContext db,
        ITenantContext tenant,
        IOptions<ScimOptions> options,
        IClock clock,
        string scope,
        CancellationToken ct,
        Func<ScimToken, Task<IResult>> next)
    {
        if (!options.Value.Enabled)
        {
            return Error(404, null, "ScimDisabled");
        }

        var raw = ReadBearer(http);
        if (raw is null)
        {
            return Error(401, "invalidToken", "InvalidToken");
        }

        await RlsSession.SetScimTokenHashAsync(db, ScimTokenCodec.Hash(raw), ct);
        var token = await db.ScimTokens.FirstOrDefaultAsync(x => x.TokenHash == ScimTokenCodec.Hash(raw), ct);
        if (token is null || token.RevokedAt is not null || !token.Scopes.Contains(scope, StringComparison.Ordinal))
        {
            return token is null || token.RevokedAt is not null
                ? Error(401, "invalidToken", "InvalidToken")
                : Error(403, null, "InsufficientScope");
        }

        tenant.SetTenant(token.TenantId);
        await RlsSession.EnsureAppliedAsync(db, tenant, ct);
        token.LastUsedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return await next(token);
    }

    private static async Task<IResult> Page<T>(
        HttpContext http,
        IQueryable<T> query,
        Func<T, JsonObject> map,
        CancellationToken ct)
    {
        int? startRaw = int.TryParse(http.Request.Query["startIndex"], out var s) ? s : null;
        int? countRaw = int.TryParse(http.Request.Query["count"], out var c) ? c : null;
        var (start, count) = ScimPolicies.NormalizePage(startRaw, countRaw);
        var total = await query.CountAsync(ct);
        var rows = await query.Skip(start - 1).Take(count).ToListAsync(ct);
        var resources = new JsonArray();
        foreach (var row in rows)
        {
            resources.Add(map(row));
        }

        return Results.Json(new JsonObject
        {
            ["schemas"] = Arr(ScimSchemas.List),
            ["totalResults"] = total,
            ["startIndex"] = start,
            ["itemsPerPage"] = rows.Count,
            ["Resources"] = resources
        });
    }

    private static ProvisionUserCommand UserCommand(ScimToken token, Guid? id, JsonElement body, bool active) =>
        new(
            token.TenantId,
            id,
            ReadString(body, "externalId"),
            ReadString(body, "userName"),
            ReadName(body),
            active,
            "scim",
            token.CreatedByUserId);

    private static ProvisionGroupCommand GroupCommand(ScimToken token, Guid? id, JsonElement body, bool remove)
    {
        IReadOnlyList<Guid>? members = body.ValueKind == JsonValueKind.Object && TryGet(body, "members", out var node)
            ? ReadMembers(node)
            : null;
        return new ProvisionGroupCommand(
            token.TenantId,
            id,
            ReadString(body, "externalId"),
            ReadString(body, "displayName"),
            members,
            remove,
            "scim",
            token.CreatedByUserId);
    }

    private static IResult UserResult(ProvisionUserResult result, bool created)
    {
        if (!result.Ok)
        {
            return Error(StatusFor(result.Error), result.Error is "UserNameConflict" or "ExternalIdConflict" ? "uniqueness" : "invalidValue", result.Error);
        }

        var json = UserNode(result);
        return created
            ? Results.Json(json, statusCode: StatusCodes.Status201Created)
            : Results.Json(json);
    }

    private static IResult GroupResult(ProvisionGroupResult result, bool created)
    {
        if (!result.Ok)
        {
            return Error(StatusFor(result.Error), result.Error == "GroupNotFound" ? null : "invalidValue", result.Error);
        }

        return created
            ? Results.Json(GroupNode(result), statusCode: StatusCodes.Status201Created)
            : Results.Json(GroupNode(result));
    }

    private static JsonObject UserNode(ProvisionUserResult result) => new()
    {
        ["schemas"] = Arr(ScimSchemas.User),
        ["id"] = result.PrincipalId.ToString(),
        ["externalId"] = result.ExternalId,
        ["userName"] = result.UserName,
        ["name"] = new JsonObject { ["formatted"] = result.DisplayName },
        ["active"] = result.Active,
        ["meta"] = new JsonObject { ["resourceType"] = "User", ["location"] = $"/scim/v2/Users/{result.PrincipalId}" }
    };

    private static JsonObject UserJson(ScimPrincipal row) => new()
    {
        ["schemas"] = Arr(ScimSchemas.User),
        ["id"] = row.Id.ToString(),
        ["externalId"] = row.ExternalId,
        ["userName"] = row.UserName,
        ["name"] = new JsonObject { ["formatted"] = row.DisplayName },
        ["active"] = row.Active,
        ["meta"] = new JsonObject { ["resourceType"] = "User", ["location"] = $"/scim/v2/Users/{row.Id}" }
    };

    private static JsonObject GroupNode(ProvisionGroupResult result) => new()
    {
        ["schemas"] = Arr(ScimSchemas.Group),
        ["id"] = result.GroupId.ToString(),
        ["externalId"] = result.ExternalId,
        ["displayName"] = result.DisplayName,
        ["members"] = MemberArray(result.MemberIds),
        ["meta"] = new JsonObject { ["resourceType"] = "Group", ["location"] = $"/scim/v2/Groups/{result.GroupId}" }
    };

    private static JsonObject GroupJson(ScimGroup row, IReadOnlyList<Guid> members) => new()
    {
        ["schemas"] = Arr(ScimSchemas.Group),
        ["id"] = row.Id.ToString(),
        ["externalId"] = row.ExternalId,
        ["displayName"] = row.DisplayName,
        ["members"] = MemberArray(members),
        ["meta"] = new JsonObject { ["resourceType"] = "Group", ["location"] = $"/scim/v2/Groups/{row.Id}" }
    };

    private static JsonArray MemberArray(IReadOnlyList<Guid> members)
    {
        var array = new JsonArray();
        foreach (var id in members)
        {
            array.Add(new JsonObject { ["value"] = id.ToString() });
        }

        return array;
    }

    private static bool TryApplyPatch(
        JsonElement body,
        string currentUser,
        string currentDisplay,
        string? currentExternal,
        bool currentActive,
        out string userName,
        out string display,
        out string? externalId,
        out bool active,
        out string error)
    {
        userName = currentUser;
        display = currentDisplay;
        externalId = currentExternal;
        active = currentActive;
        error = "";
        if (!TryGet(body, "Operations", out var ops) || ops.ValueKind != JsonValueKind.Array)
        {
            error = "UnsupportedPatch";
            return false;
        }

        foreach (var op in ops.EnumerateArray())
        {
            if (!string.Equals(ReadString(op, "op"), "replace", StringComparison.OrdinalIgnoreCase))
            {
                error = "UnsupportedPatch";
                return false;
            }

            var path = ReadString(op, "path") ?? "";
            if (!TryGet(op, "value", out var value))
            {
                error = "UnsupportedPatch";
                return false;
            }

            switch (path)
            {
                case "active":
                    if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    {
                        error = "UnsupportedPatch";
                        return false;
                    }

                    active = value.GetBoolean();
                    break;
                case "userName":
                    userName = value.GetString() ?? "";
                    break;
                case "displayName":
                case "name.formatted":
                    display = value.GetString() ?? "";
                    break;
                case "externalId":
                    externalId = value.GetString();
                    break;
                default:
                    error = "UnsupportedPatch";
                    return false;
            }
        }

        return true;
    }

    private static List<Guid> ReadMembers(JsonElement node)
    {
        var ids = new List<Guid>();
        if (node.ValueKind != JsonValueKind.Array)
        {
            return ids;
        }

        foreach (var item in node.EnumerateArray())
        {
            var raw = item.ValueKind == JsonValueKind.Object ? ReadString(item, "value") : item.GetString();
            if (Guid.TryParse(raw, out var id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    private static string? ReadBearer(HttpContext http)
    {
        var header = http.Request.Headers.Authorization.ToString();
        if (!AuthenticationHeaderValue.TryParse(header, out var parsed))
        {
            return null;
        }

        if (!string.Equals(parsed.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = parsed.Parameter?.Trim();
        return token is not null && token.StartsWith(ScimPolicies.TokenPrefix, StringComparison.Ordinal) ? token : null;
    }

    private static string? ReadString(JsonElement body, string name) =>
        TryGet(body, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool TryGet(JsonElement body, string name, out JsonElement value)
    {
        if (body.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in body.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string? ReadName(JsonElement body)
    {
        if (body.ValueKind == JsonValueKind.Object
            && TryGet(body, "name", out var name)
            && name.ValueKind == JsonValueKind.Object
            && TryGet(name, "formatted", out var formatted))
        {
            return formatted.GetString();
        }

        return ReadString(body, "displayName");
    }

    private static bool? ReadBool(JsonElement body, string name)
    {
        if (!TryGet(body, name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static int StatusFor(string? error) => error switch
    {
        "PrincipalNotFound" or "GroupNotFound" or "MappingNotFound" => 404,
        "UserNameConflict" or "ExternalIdConflict" or "MappingRoleConflict" => 409,
        "InsufficientScope" => 403,
        _ => 400
    };

    private static IResult Error(int status, string? scimType, string? detail) =>
        Results.Json(ErrorNode(status, scimType, detail), statusCode: status);

    private static JsonObject ErrorNode(int status, string? scimType, string? detail)
    {
        var node = new JsonObject
        {
            ["schemas"] = Arr(ScimSchemas.Error),
            ["status"] = status.ToString(),
            ["detail"] = detail ?? "ScimError"
        };
        if (scimType is not null)
        {
            node["scimType"] = scimType;
        }

        return node;
    }

    private static JsonArray Arr(string value)
    {
        var array = new JsonArray();
        array.Add(JsonValue.Create(value));
        return array;
    }

    private sealed record BulkItem(int Status, string? Location, JsonNode? Body);
}
