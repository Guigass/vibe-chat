using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using VibeChat.BuildingBlocks;
using VibeChat.Identity;
using VibeChat.Infrastructure;
using VibeChat.SharedKernel;

namespace VibeChat.Api.Endpoints;

internal static class DesktopEndpoints
{
    private const int MaxBodyBytes = 8192;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static void MapDesktop(this RouteGroupBuilder v1)
    {
        v1.MapGet("/desktop/shell", ReadShellAsync)
            .RequirePermission(Permissions.Message.Read);
        v1.MapPost("/desktop/deep-links/validate", ValidateLinkAsync)
            .RequirePermission(Permissions.Message.Read);
        v1.MapPost("/desktop/updates/verify", VerifyUpdateAsync)
            .RequirePermission(Permissions.Message.Read);
        v1.MapGet("/desktop/devices", ListDevicesAsync)
            .RequirePermission(Permissions.Message.Read);
        v1.MapPost("/desktop/devices", RegisterAsync)
            .RequirePermission(Permissions.Message.Read);
        v1.MapPut("/desktop/devices/{deviceId:guid}/preferences", PreferencesAsync)
            .RequirePermission(Permissions.Message.Read);
        v1.MapDelete("/desktop/devices/{deviceId:guid}", LogoutAsync)
            .RequirePermission(Permissions.Message.Read);
    }

    private static IResult ReadShellAsync(IOptions<DesktopOptions> options)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        return Results.Ok(DescribeSafe(options.Value));
    }

    private static async Task<IResult> ValidateLinkAsync(HttpContext http, IOptions<DesktopOptions> options, CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        if (await TryBodyAsync(http.Request, ct) is not { } json)
        {
            return Results.Json(new { error = DesktopErrors.BodyInvalid }, statusCode: StatusCodes.Status400BadRequest);
        }

        if (DesktopRules.ContainsSecret(json))
        {
            return Results.Json(new { error = DesktopErrors.TokenRejected }, statusCode: StatusCodes.Status400BadRequest);
        }

        LinkBody? body;
        try
        {
            body = JsonSerializer.Deserialize<LinkBody>(json, Json);
        }
        catch (JsonException)
        {
            return Results.Json(new { error = DesktopErrors.DeepLinkRejected }, statusCode: StatusCodes.Status400BadRequest);
        }

        if (!DesktopRules.TryParseDeepLink(body?.Url, options.Value.AllowedHosts, out var view, out var error))
        {
            return Results.Json(new { error }, statusCode: StatusCodes.Status400BadRequest);
        }

        return Results.Ok(new { accepted = true, host = view!.Host, kind = view.Kind, id = view.Id });
    }

    private static async Task<IResult> VerifyUpdateAsync(HttpContext http, IOptions<DesktopOptions> options, CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        if (await TryBodyAsync(http.Request, ct) is not { } json)
        {
            return Results.Json(new { error = DesktopErrors.UpdateRejected }, statusCode: StatusCodes.Status400BadRequest);
        }

        if (!DesktopRules.TryVerifyUpdate(json, options.Value, out var view, out var error))
        {
            var status = error == DesktopErrors.UpdateOff ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest;
            return Results.Json(new { error }, statusCode: status);
        }

        return Results.Ok(new { accepted = true, productVersion = view!.ProductVersion, feedHost = view.FeedHost, targets = view.Targets });
    }

    private static async Task<IResult> ListDevicesAsync(IOptions<DesktopOptions> options, DesktopClientService desktop, CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        return Results.Ok(await desktop.ListAsync(ct));
    }

    private static async Task<IResult> RegisterAsync(HttpContext http, IOptions<DesktopOptions> options, DesktopClientService desktop, CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        if (!TryKey(http, out var key))
        {
            return Results.Json(new { error = DesktopErrors.IdempotencyKeyRequired }, statusCode: StatusCodes.Status400BadRequest);
        }

        if (await TryBodyAsync(http.Request, ct) is not { } json)
        {
            return Results.Json(new { error = DesktopErrors.BodyInvalid }, statusCode: StatusCodes.Status400BadRequest);
        }

        if (DesktopRules.ContainsSecret(json))
        {
            return Results.Json(new { error = DesktopErrors.TokenRejected }, statusCode: StatusCodes.Status400BadRequest);
        }

        RegisterBody? body;
        try
        {
            body = JsonSerializer.Deserialize<RegisterBody>(json, Json);
        }
        catch (JsonException)
        {
            return Results.Json(new { error = DesktopErrors.BodyInvalid }, statusCode: StatusCodes.Status400BadRequest);
        }

        return Written(await desktop.RegisterAsync(body?.Platform, body?.AppVersion, key, ct));
    }

    private static async Task<IResult> PreferencesAsync(
        Guid deviceId,
        HttpContext http,
        IOptions<DesktopOptions> options,
        DesktopClientService desktop,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
        {
            return Disabled();
        }

        if (!TryKey(http, out var key))
        {
            return Results.Json(new { error = DesktopErrors.IdempotencyKeyRequired }, statusCode: StatusCodes.Status400BadRequest);
        }

        if (await TryBodyAsync(http.Request, ct) is not { } json)
        {
            return Results.Json(new { error = DesktopErrors.BodyInvalid }, statusCode: StatusCodes.Status400BadRequest);
        }

        if (DesktopRules.ContainsSecret(json))
        {
            return Results.Json(new { error = DesktopErrors.TokenRejected }, statusCode: StatusCodes.Status400BadRequest);
        }

        PreferenceBody? body;
        try
        {
            body = JsonSerializer.Deserialize<PreferenceBody>(json, Json);
        }
        catch (JsonException)
        {
            return Results.Json(new { error = DesktopErrors.PreferenceInvalid }, statusCode: StatusCodes.Status400BadRequest);
        }

        if (!DesktopRules.TryPreferences(body?.LaunchAtLogin, body?.Notifications, body?.Tray))
        {
            return Results.Json(new { error = DesktopErrors.PreferenceInvalid }, statusCode: StatusCodes.Status400BadRequest);
        }

        return Written(await desktop.PreferencesAsync(deviceId, body!.LaunchAtLogin!.Value, body.Notifications!.Value, body.Tray!.Value, key, ct));
    }

    private static async Task<IResult> LogoutAsync(
        Guid deviceId,
        HttpContext http,
        IOptions<DesktopOptions> options,
        DesktopClientService desktop,
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
            return Results.Json(new { error = DesktopErrors.IdempotencyKeyRequired }, statusCode: StatusCodes.Status400BadRequest);
        }

        var admin = await permissions.HasPermissionAsync(tenant.TenantId, tenant.UserId, Permissions.Workspace.Admin, ct);
        return Written(await desktop.LogoutAsync(deviceId, admin, key, ct));
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

    private static IResult Written(DesktopWriteResult result) =>
        result.Error is null
            ? Results.Ok(result.Device)
            : Results.Json(new { error = result.Error }, statusCode: result.Status);

    private static IResult Disabled() =>
        Results.Json(new { error = DesktopErrors.Disabled }, statusCode: StatusCodes.Status404NotFound);

    private static object DescribeSafe(DesktopOptions options)
    {
        var shell = DesktopRules.Describe(options);
        return new
        {
            shell.Wrapper,
            shell.WrapperVersion,
            shell.UiFork,
            shell.ApiBase,
            shell.Scheme,
            shell.SchemeVersion,
            shell.Targets,
            shell.Capabilities,
            shell.LaunchAtLoginDefault,
            shell.Updates,
            shell.SignedBuilds,
            shell.Signing,
            shell.BrowserFallback,
            shell.PackageHashes
        };
    }

    private sealed class LinkBody
    {
        public string? Url { get; set; }
    }

    private sealed class RegisterBody
    {
        public string? Platform { get; set; }
        public string? AppVersion { get; set; }
    }

    private sealed class PreferenceBody
    {
        public bool? LaunchAtLogin { get; set; }
        public bool? Notifications { get; set; }
        public bool? Tray { get; set; }
    }
}
