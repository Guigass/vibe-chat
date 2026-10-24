using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using VibeChat.BuildingBlocks;
using VibeChat.Identity;
using VibeChat.Infrastructure;
using VibeChat.SharedKernel;

namespace VibeChat.Api.Endpoints;

internal static class MobileEndpoints
{
    private const int MaxBodyBytes = 8192;

    internal static void MapMobile(this RouteGroupBuilder v1)
    {
        v1.MapGet("/mobile/shell", ReadShellAsync)
            .RequirePermission(Permissions.Message.Read);
        v1.MapPost("/mobile/deep-links/validate", ValidateLinkAsync)
            .RequirePermission(Permissions.Message.Read);
        v1.MapPost("/mobile/share/validate", ValidateShareAsync)
            .RequirePermission(Permissions.Message.Read);
        v1.MapPost("/mobile/push/preview", PreviewPushAsync)
            .RequirePermission(Permissions.Message.Read);
        v1.MapGet("/mobile/devices", ListDevicesAsync)
            .RequirePermission(Permissions.Message.Read);
        v1.MapPost("/mobile/devices", RegisterAsync)
            .RequirePermission(Permissions.Message.Read);
        v1.MapPut("/mobile/devices/{deviceId:guid}/preferences", PreferencesAsync)
            .RequirePermission(Permissions.Message.Read);
        v1.MapDelete("/mobile/devices/{deviceId:guid}", LogoutAsync)
            .RequirePermission(Permissions.Message.Read);
    }

    private static IResult ReadShellAsync(IOptions<MobileOptions> options)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        return Results.Ok(MobileRules.Describe(options.Value));
    }

    private static async Task<IResult> ValidateLinkAsync(HttpContext http, IOptions<MobileOptions> options, CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        if (await TryBodyAsync(http.Request, ct) is not { } json)
        {
            return Results.Json(new { error = MobileErrors.BodyInvalid }, statusCode: StatusCodes.Status400BadRequest);
        }

        if (!MobileRules.TryParseDeepLink(json, options.Value.AllowedHosts, out var view, out var error))
        {
            return Results.Json(new { error }, statusCode: StatusCodes.Status400BadRequest);
        }

        return Results.Ok(new { accepted = true, host = view!.Host, kind = view.Kind, id = view.Id });
    }

    private static async Task<IResult> ValidateShareAsync(HttpContext http, IOptions<MobileOptions> options, CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        if (await TryBodyAsync(http.Request, ct) is not { } json)
        {
            return Results.Json(new { error = MobileErrors.BodyInvalid }, statusCode: StatusCodes.Status400BadRequest);
        }

        if (!MobileRules.TryAcceptShare(json, options.Value.AllowedHosts, out var view, out var error))
        {
            return Results.Json(new { error }, statusCode: StatusCodes.Status400BadRequest);
        }

        return Results.Ok(new { accepted = true, host = view!.Host, kind = view.Kind, id = view.Id, sendsMessage = view.SendsMessage });
    }

    private static async Task<IResult> PreviewPushAsync(HttpContext http, IOptions<MobileOptions> options, CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        if (await TryBodyAsync(http.Request, ct) is not { } json)
        {
            return Results.Json(new { error = MobileErrors.BodyInvalid }, statusCode: StatusCodes.Status400BadRequest);
        }

        if (!MobileRules.TryAcceptPush(json, out var view, out var error))
        {
            return Results.Json(new { error }, statusCode: StatusCodes.Status400BadRequest);
        }

        return Results.Ok(new { accepted = true, kind = view!.Kind, conversationId = view.ConversationId, containsContent = view.ContainsContent });
    }

    private static async Task<IResult> ListDevicesAsync(IOptions<MobileOptions> options, MobileClientService mobile, CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        return Results.Ok(await mobile.ListAsync(ct));
    }

    private static async Task<IResult> RegisterAsync(HttpContext http, IOptions<MobileOptions> options, MobileClientService mobile, CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        if (!TryKey(http, out var key))
        {
            return Results.Json(new { error = MobileErrors.IdempotencyKeyRequired }, statusCode: StatusCodes.Status400BadRequest);
        }

        if (await TryBodyAsync(http.Request, ct) is not { } json)
        {
            return Results.Json(new { error = MobileErrors.BodyInvalid }, statusCode: StatusCodes.Status400BadRequest);
        }

        if (MobileRules.ContainsSecret(json))
        {
            return Results.Json(new { error = MobileErrors.TokenRejected }, statusCode: StatusCodes.Status400BadRequest);
        }

        if (!MobileRules.IsRegisterBody(json))
        {
            return Results.Json(new { error = MobileErrors.BodyInvalid }, statusCode: StatusCodes.Status400BadRequest);
        }

        RegisterBody? body;
        try
        {
            body = JsonSerializer.Deserialize<RegisterBody>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException)
        {
            return Results.Json(new { error = MobileErrors.BodyInvalid }, statusCode: StatusCodes.Status400BadRequest);
        }

        return Written(await mobile.RegisterAsync(body?.Platform, body?.AppVersion, key, ct));
    }

    private static async Task<IResult> PreferencesAsync(
        Guid deviceId,
        HttpContext http,
        IOptions<MobileOptions> options,
        MobileClientService mobile,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        if (!TryKey(http, out var key))
        {
            return Results.Json(new { error = MobileErrors.IdempotencyKeyRequired }, statusCode: StatusCodes.Status400BadRequest);
        }

        if (await TryBodyAsync(http.Request, ct) is not { } json)
        {
            return Results.Json(new { error = MobileErrors.BodyInvalid }, statusCode: StatusCodes.Status400BadRequest);
        }

        if (MobileRules.ContainsSecret(json))
        {
            return Results.Json(new { error = MobileErrors.TokenRejected }, statusCode: StatusCodes.Status400BadRequest);
        }

        if (!MobileRules.IsPreferenceBody(json))
        {
            return Results.Json(new { error = MobileErrors.PreferenceInvalid }, statusCode: StatusCodes.Status400BadRequest);
        }

        PreferenceBody? body;
        try
        {
            body = JsonSerializer.Deserialize<PreferenceBody>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException)
        {
            return Results.Json(new { error = MobileErrors.PreferenceInvalid }, statusCode: StatusCodes.Status400BadRequest);
        }

        if (!MobileRules.TryPreferences(body?.BiometricLock, body?.Notifications, body?.Camera, body?.FilePicker))
        {
            return Results.Json(new { error = MobileErrors.PreferenceInvalid }, statusCode: StatusCodes.Status400BadRequest);
        }

        return Written(await mobile.PreferencesAsync(
            deviceId,
            body!.BiometricLock!.Value,
            body.Notifications!.Value,
            body.Camera!.Value,
            body.FilePicker!.Value,
            key,
            ct));
    }

    private static async Task<IResult> LogoutAsync(
        Guid deviceId,
        HttpContext http,
        IOptions<MobileOptions> options,
        MobileClientService mobile,
        IPermissionChecker permissions,
        ITenantContext tenant,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        if (!TryKey(http, out var key))
        {
            return Results.Json(new { error = MobileErrors.IdempotencyKeyRequired }, statusCode: StatusCodes.Status400BadRequest);
        }

        var admin = await permissions.HasPermissionAsync(tenant.TenantId, tenant.UserId, Permissions.Workspace.Admin, ct);
        return Written(await mobile.LogoutAsync(deviceId, admin, key, ct));
    }

    private static async Task<string?> TryBodyAsync(HttpRequest request, CancellationToken ct)
    {
        request.EnableBuffering();
        using var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
        var buffer = new char[MaxBodyBytes + 1];
        var read = await reader.ReadBlockAsync(buffer.AsMemory(0, buffer.Length), ct);
        if (read > MaxBodyBytes)
        {
            return null;
        }

        return new string(buffer, 0, read);
    }

    private static bool TryKey(HttpContext http, out string key)
    {
        key = http.Request.Headers["Idempotency-Key"].ToString().Trim();
        return key.Length is > 0 and <= 120;
    }

    private static IResult Written(MobileWriteResult result) =>
        result.Error is null
            ? Results.Ok(result.Device)
            : Results.Json(new { error = result.Error }, statusCode: result.Status);

    private static IResult Disabled() =>
        Results.Json(new { error = MobileErrors.Disabled }, statusCode: StatusCodes.Status404NotFound);

    private sealed class RegisterBody
    {
        public string? Platform { get; set; }
        public string? AppVersion { get; set; }
    }

    private sealed class PreferenceBody
    {
        public bool? BiometricLock { get; set; }
        public bool? Notifications { get; set; }
        public bool? Camera { get; set; }
        public bool? FilePicker { get; set; }
    }
}
