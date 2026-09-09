using System.Security.Claims;
using Asp.Versioning;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using VibeChat.Api;
using VibeChat.Api.Endpoints;
using VibeChat.BuildingBlocks;
using VibeChat.Infrastructure;
using VibeChat.SharedKernel;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
});

var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
    ?? ["http://localhost:4200", "https://localhost:4200", "https://localhost:8443"];
builder.Services.AddCors(options =>
{
    options.AddPolicy("localhost", policy => policy
        .WithOrigins(corsOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    // Reference proxy may sit on Docker bridge / host network; clear known nets for self-host.
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var auth = builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = "smart";
    options.DefaultChallengeScheme = "smart";
});

auth.AddPolicyScheme("smart", "JWT or DevAuth", options =>
{
    options.ForwardDefaultSelector = context =>
    {
        if (!builder.Environment.IsDevelopment())
        {
            return JwtBearerDefaults.AuthenticationScheme;
        }

        var hasDevHeader = context.Request.Headers.ContainsKey("X-Dev-User");
        var hasDevQuery = context.Request.Query.ContainsKey("devUser");
        return hasDevHeader || hasDevQuery
            ? DevAuthHandler.SchemeName
            : JwtBearerDefaults.AuthenticationScheme;
    };
});

auth.AddJwtBearer(options =>
{
    options.Authority = builder.Configuration["Authentication:Authority"];
    options.Audience = builder.Configuration["Authentication:Audience"];
    options.RequireHttpsMetadata = builder.Configuration.GetValue("Authentication:RequireHttpsMetadata", true);
    // Compose apps (B-074): Authority = public issuer (browser iss); MetadataAddress = in-network discovery.
    var metadataAddress = builder.Configuration["Authentication:MetadataAddress"];
    if (!string.IsNullOrWhiteSpace(metadataAddress))
    {
        options.MetadataAddress = metadataAddress;
    }

    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
            {
                context.Token = accessToken;
            }

            return Task.CompletedTask;
        }
    };
});
auth.AddScheme<AuthenticationSchemeOptions, DevAuthHandler>(DevAuthHandler.SchemeName, _ => { });

builder.Services.AddAuthorization();
builder.Services.AddScoped<ICurrentUser>(sp =>
{
    var accessor = sp.GetRequiredService<IHttpContextAccessor>();
    return new CurrentUser(accessor.HttpContext?.User ?? new ClaimsPrincipal());
});

builder.Services.AddVibeChatInfrastructure(builder.Configuration);

// BUG-006: keep-alive 15s + client timeout 90s tolerates brief idle/background
// without premature disconnect; client serverTimeout aligns to 90s.
var signalR = builder.Services
    .AddSignalR(options =>
    {
        options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        options.ClientTimeoutInterval = TimeSpan.FromSeconds(90);
        options.HandshakeTimeout = TimeSpan.FromSeconds(15);
    })
    .AddMessagePackProtocol();
var redisConnection = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrWhiteSpace(redisConnection))
{
    signalR.AddStackExchangeRedis(redisConnection);
}

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("VibeChat.Api"))
    .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation())
    .WithMetrics(metrics => metrics.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddMeter(VibeChatMetrics.MeterName));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// SEC-RLS-RUNTIME: roles + migrator migrate/RLS catalog + optional seed; runtime role validated.
if (app.Configuration.GetValue("Bootstrap:Enabled", false)
    || app.Configuration.GetValue("Seed:Enabled", false)
    || app.Configuration.GetValue("Database:BootstrapOnStartup", app.Environment.IsDevelopment()))
{
    await DatabaseBootstrap.MigrateSeedAndProtectAsync(
        app.Services,
        app.Configuration,
        app.Environment,
        CancellationToken.None);
}
else
{
    await DatabaseBootstrap.ValidateRuntimeRoleAsync(
        DatabaseBootstrap.ResolveRuntimeConnectionString(app.Configuration),
        app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseBootstrap"),
        CancellationToken.None);
}

app.UseExceptionHandler();
app.UseForwardedHeaders();
app.UseCors("localhost");
app.UseAuthentication();
app.UseAuthorization();
// SEC-RLS-RUNTIME: EnsureApplied opens a txn + SET LOCAL; commit/rollback at end of HTTP request.
app.Use(async (context, next) =>
{
    try
    {
        await next();
        var db = context.RequestServices.GetService<VibeChatDbContext>();
        if (db is not null)
        {
            await RlsSession.CommitAsync(db, context.RequestAborted);
        }
    }
    catch
    {
        var db = context.RequestServices.GetService<VibeChatDbContext>();
        if (db is not null)
        {
            await RlsSession.RollbackAsync(db, CancellationToken.None);
        }

        throw;
    }
});

// B-174: group filter enforces RequirePermissionAttribute metadata (fail-closed).
var v1 = app.MapGroup("/api/v1").RequireAuthorization().AddEndpointFilter<RequirePermissionFilter>();

v1.MapIdentity();
v1.MapWorkspaces();
v1.MapChannelLists();
v1.MapSpacesAndMembers();
v1.MapChannelMembers();
v1.MapWorkspaceRoles();
v1.MapPresence();
v1.MapChannelCreation();
v1.MapCommands();
v1.MapPolls();
v1.MapMessages();
v1.MapThreads();
v1.MapFiles();
v1.MapMessageActions();
v1.MapReadCursor();
v1.MapSearch();
v1.MapUnreadCount();
v1.MapNotifications();
v1.MapAdministration();
v1.MapAI();
v1.MapDevelopment(app);

app.MapHub<ChatHub>("/hubs/chat").RequireAuthorization();
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready");
app.MapHealthChecks("/ready"); // ops alias (runbooks / Compose)

app.Run();

public partial class Program;
