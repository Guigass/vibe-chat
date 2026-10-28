using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using VibeChat.Realtime;

namespace VibeChat.Infrastructure;

/// <summary>
/// Live SFU health is degraded, never unhealthy, so a dead provider does not fail chat readiness.
/// </summary>
public sealed class LiveMediaHealthCheck(ILiveMediaProvider provider, IOptions<LiveOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!options.Value.Enabled)
        {
            return HealthCheckResult.Healthy("live disabled");
        }

        var status = await provider.StatusAsync(cancellationToken);
        return status.Healthy
            ? HealthCheckResult.Healthy(status.Detail)
            : HealthCheckResult.Degraded(status.Detail);
    }
}
