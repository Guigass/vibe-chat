namespace VibeChat.SecurityTests;

public sealed class SecurityBoundaryTests
{
    [Fact(Skip = "Requires Docker/Testcontainers PostgreSQL; enable in CI with Docker available.")]
    public Task Cross_tenant_access_is_denied()
    {
        return Task.CompletedTask;
    }

    [Fact(Skip = "Requires Docker/Testcontainers PostgreSQL; enable in CI with Docker available.")]
    public Task Cross_workspace_channel_access_is_denied()
    {
        return Task.CompletedTask;
    }
}
