using VibeChat.Infrastructure;

namespace VibeChat.Api;

/// <summary>
/// B-144: a draining node fails readiness and still finishes the request that is
/// already inside the process. Live stays up. Standard (flag off) does not count
/// in-flight work.
/// </summary>
public sealed class HaDrainMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, HaAdmission admission)
    {
        if (admission.Draining && IsReadyPath(context.Request.Path))
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        if (!admission.TracksRequests)
        {
            await next(context);
            return;
        }

        admission.Enter();
        try
        {
            await next(context);
        }
        finally
        {
            admission.Exit();
        }
    }

    private static bool IsReadyPath(PathString path) =>
        path.Equals("/health/ready", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/ready", StringComparison.OrdinalIgnoreCase);
}
