using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using VibeChat.BuildingBlocks;
using VibeChat.SharedKernel;
using VibeChat.TestHost;

namespace VibeChat.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class EndpointRegistrationIntegrationTests(VibeChatApiFactory factory)
{
    [Fact]
    public void Module_maps_preserve_v1_routes_order_and_authorization_metadata()
    {
        using var client = factory.CreateClient();
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api/v1", StringComparison.Ordinal) == true)
            .ToArray();

        // Baseline registrations from before B-178; the gate follow-up explicitly annotates PUT /me.
        // Do not derive expectations from the maps under test.
        EndpointContract[] expected =
        [
            new("GET", "/api/v1/me", "", false, ""),
            new("PUT", "/api/v1/me", "", false, "caller-only profile locale update"),
            new("GET", "/api/v1/workspaces", "", false, ""),
            new("GET", "/api/v1/workspaces/{workspaceId:guid}/channels", "", false, ""),
            new("GET", "/api/v1/workspaces/{workspaceId:guid}/channels/unread", "", false, ""),
            new("GET", "/api/v1/workspaces/{workspaceId:guid}/spaces", "", false, ""),
            new("POST", "/api/v1/workspaces/{workspaceId:guid}/spaces", string.Join(",", new[] { Permissions.Channel.Create }), false, ""),
            new("GET", "/api/v1/workspaces/{workspaceId:guid}/members", "", false, ""),
            new("GET", "/api/v1/workspaces/{workspaceId:guid}/channels/{channelId:guid}/members", "", false, ""),
            new("GET", "/api/v1/workspaces/{workspaceId:guid}/roles", string.Join(",", new[] { Permissions.Workspace.Admin }), false, ""),
            new("POST", "/api/v1/workspaces/{workspaceId:guid}/members", string.Join(",", new[] { Permissions.Workspace.Admin }), false, ""),
            new("PUT", "/api/v1/workspaces/{workspaceId:guid}/members/{userId:guid}/role", string.Join(",", new[] { Permissions.Workspace.Admin }), false, ""),
            new("GET", "/api/v1/workspaces/{workspaceId:guid}/presence", "", false, ""),
            new("POST", "/api/v1/workspaces/{workspaceId:guid}/dms", "", false, "membership-only open DM (B-021)"),
            new("POST", "/api/v1/workspaces/{workspaceId:guid}/group-dms", "", false, "membership-only group DM (B-101)"),
            new("POST", "/api/v1/channels/{channelId:guid}/participants", "", false, "membership-only group DM add (B-101)"),
            new("DELETE", "/api/v1/channels/{channelId:guid}/participants/me", "", false, "membership-only group DM leave (B-101)"),
            new("PATCH", "/api/v1/channels/{channelId:guid}", "", false, "membership-only group DM rename (B-101)"),
            new("POST", "/api/v1/workspaces/{workspaceId:guid}/channels", string.Join(",", new[] { Permissions.Channel.Create }), false, ""),
            new("PUT", "/api/v1/workspaces/{workspaceId:guid}/channels/{channelId:guid}/topic", string.Join(",", new[] { Permissions.Channel.Create }), false, ""),
            new("GET", "/api/v1/workspaces/{workspaceId:guid}/commands", "", false, ""),
            new("POST", "/api/v1/channels/{channelId:guid}/polls", string.Join(",", new[] { Permissions.Message.Send }), false, ""),
            new("POST", "/api/v1/polls/{pollId:guid}/votes", string.Join(",", new[] { Permissions.Message.Send }), false, ""),
            new("DELETE", "/api/v1/polls/{pollId:guid}/votes", string.Join(",", new[] { Permissions.Message.Send }), false, ""),
            new("POST", "/api/v1/polls/{pollId:guid}/close", string.Join(",", new[] { Permissions.Message.Send }), false, ""),
            new("GET", "/api/v1/channels/{channelId:guid}/messages", "", false, ""),
            new("POST", "/api/v1/channels/{channelId:guid}/messages", string.Join(",", new[] { Permissions.Message.Send }), false, ""),
            new("POST", "/api/v1/workspaces/{workspaceId:guid}/messages/{messageId:guid}/forward", string.Join(",", new[] { Permissions.Message.Send }), false, ""),
            new("POST", "/api/v1/channels/{channelId:guid}/messages/{messageId:guid}/threads", string.Join(",", new[] { Permissions.Message.Send }), false, ""),
            new("GET", "/api/v1/threads/{threadId:guid}", "", false, ""),
            new("GET", "/api/v1/threads/{threadId:guid}/messages", "", false, ""),
            new("POST", "/api/v1/threads/{threadId:guid}/messages", string.Join(",", new[] { Permissions.Message.Send }), false, ""),
            new("POST", "/api/v1/threads/{threadId:guid}/subscription", string.Join(",", new[] { Permissions.Message.Read }), false, ""),
            new("DELETE", "/api/v1/threads/{threadId:guid}/subscription", string.Join(",", new[] { Permissions.Message.Read }), false, ""),
            new("PUT", "/api/v1/threads/{threadId:guid}/subscription/read-cursor", string.Join(",", new[] { Permissions.Message.Read }), false, ""),
            new("GET", "/api/v1/workspaces/{workspaceId:guid}/threads/following", string.Join(",", new[] { Permissions.Message.Read }), false, ""),
            new("POST", "/api/v1/threads/{threadId:guid}/messages/{messageId:guid}/share-to-channel", string.Join(",", new[] { Permissions.Message.Send }), false, ""),
            new("POST", "/api/v1/channels/{channelId:guid}/attachments", string.Join(",", new[] { Permissions.Files.Upload, Permissions.Message.Send }), false, ""),
            new("POST", "/api/v1/channels/{channelId:guid}/attachments/{attachmentId:guid}/complete", string.Join(",", new[] { Permissions.Files.Upload }), false, ""),
            new("GET", "/api/v1/channels/{channelId:guid}/attachments/{attachmentId:guid}/download", string.Join(",", new[] { Permissions.Files.Download, Permissions.Message.Read }), false, ""),
            new("GET", "/api/v1/channels/{channelId:guid}/attachments/{attachmentId:guid}/thumbnail", string.Join(",", new[] { Permissions.Files.Download, Permissions.Message.Read }), false, ""),
            new("PUT", "/api/v1/channels/{channelId:guid}/messages/{messageId:guid}", "", false, "conditional EditOwn vs authorship (B-023)"),
            new("PUT", "/api/v1/channels/{channelId:guid}/messages/{messageId:guid}/reactions", string.Join(",", new[] { Permissions.Message.React }), false, ""),
            new("POST", "/api/v1/channels/{channelId:guid}/messages/{messageId:guid}/pin", string.Join(",", new[] { Permissions.Message.Pin }), false, ""),
            new("DELETE", "/api/v1/channels/{channelId:guid}/messages/{messageId:guid}/pin", string.Join(",", new[] { Permissions.Message.Pin }), false, ""),
            new("GET", "/api/v1/channels/{channelId:guid}/pins", string.Join(",", new[] { Permissions.Message.Read }), false, ""),
            new("POST", "/api/v1/workspaces/{workspaceId:guid}/saved", string.Join(",", new[] { Permissions.Message.Read }), false, ""),
            new("PATCH", "/api/v1/workspaces/{workspaceId:guid}/saved/{messageId:guid}", string.Join(",", new[] { Permissions.Message.Read }), false, ""),
            new("DELETE", "/api/v1/workspaces/{workspaceId:guid}/saved/{messageId:guid}", string.Join(",", new[] { Permissions.Message.Read }), false, ""),
            new("GET", "/api/v1/workspaces/{workspaceId:guid}/saved", "", false, ""),
            new("GET", "/api/v1/channels/{channelId:guid}/messages/{messageId:guid}/reactions/{emoji}/users", string.Join(",", new[] { Permissions.Message.React }), false, ""),
            new("DELETE", "/api/v1/channels/{channelId:guid}/messages/{messageId:guid}", "", false, "conditional DeleteOwn/DeleteAny (B-023)"),
            new("DELETE", "/api/v1/channels/{channelId:guid}/messages/{messageId:guid}/link-preview", "", false, "author or workspace.admin (B-091)"),
            new("GET", "/api/v1/channels/{channelId:guid}/messages/{messageId:guid}/link-preview/image", "", false, ""),
            new("PUT", "/api/v1/channels/{channelId:guid}/read-cursor", string.Join(",", new[] { Permissions.Message.Read }), false, ""),
            new("GET", "/api/v1/search/messages", string.Join(",", new[] { Permissions.Search.Messages, Permissions.Message.Read }), false, ""),
            new("GET", "/api/v1/channels/{channelId:guid}/unread-count", "", false, ""),
            new("GET", "/api/v1/notifications/push/public-key", string.Join(",", new[] { Permissions.Message.Read }), false, ""),
            new("GET", "/api/v1/notifications/push/subscriptions", string.Join(",", new[] { Permissions.Message.Read }), false, ""),
            new("POST", "/api/v1/notifications/push/subscriptions", string.Join(",", new[] { Permissions.Message.Read }), false, ""),
            new("DELETE", "/api/v1/notifications/push/subscriptions/{id:guid}", string.Join(",", new[] { Permissions.Message.Read }), false, ""),
            new("GET", "/api/v1/notifications/preferences", string.Join(",", new[] { Permissions.Message.Read }), false, ""),
            new("PUT", "/api/v1/notifications/preferences", string.Join(",", new[] { Permissions.Message.Read }), false, ""),
            new("PUT", "/api/v1/notifications/preferences/channels/{channelId:guid}", string.Join(",", new[] { Permissions.Message.Read }), false, ""),
            new("DELETE", "/api/v1/notifications/preferences/channels/{channelId:guid}", string.Join(",", new[] { Permissions.Message.Read }), false, ""),
            new("PUT", "/api/v1/notifications/preferences/channels/{channelId:guid}/follow-all-threads", string.Join(",", new[] { Permissions.Message.Read }), false, ""),
            new("GET", "/api/v1/admin/dashboard", string.Join(",", new[] { Permissions.Admin.Dashboard }), false, ""),
            new("GET", "/api/v1/admin/audit-events", string.Join(",", new[] { Permissions.Admin.Dashboard }), false, ""),
            new("GET", "/api/v1/admin/conversations", string.Join(",", new[] { Permissions.Admin.Dashboard }), false, ""),
            new("GET", "/api/v1/admin/conversations/{channelId:guid}/messages", string.Join(",", new[] { Permissions.Admin.Dashboard }), false, ""),
            new("GET", "/api/v1/admin/threads/{threadId:guid}/messages", string.Join(",", new[] { Permissions.Admin.Dashboard }), false, ""),
            new("GET", "/api/v1/admin/settings", string.Join(",", new[] { Permissions.Workspace.Admin }), false, ""),
            new("PUT", "/api/v1/admin/settings", string.Join(",", new[] { Permissions.Workspace.Admin }), false, ""),
            new("POST", "/api/v1/admin/settings/credentials/openrouter/rotate", string.Join(",", new[] { Permissions.Workspace.Admin }), false, ""),
            new("POST", "/api/v1/admin/settings/credentials/smtp/rotate", string.Join(",", new[] { Permissions.Workspace.Admin }), false, ""),
            new("POST", "/api/v1/admin/settings/credentials/webhook/rotate", string.Join(",", new[] { Permissions.Workspace.Admin }), false, ""),
            new("POST", "/api/v1/admin/settings/credentials/vapid/rotate", string.Join(",", new[] { Permissions.Workspace.Admin }), false, ""),
            new("POST", "/api/v1/admin/settings/encryption/reencrypt", string.Join(",", new[] { Permissions.Workspace.Admin }), false, ""),
            new("GET", "/api/v1/admin/workspaces/{workspaceId:guid}/export", string.Join(",", new[] { Permissions.Workspace.Admin }), false, ""),
            new("GET", "/api/v1/admin/health-summary", string.Join(",", new[] { Permissions.Admin.Dashboard }), false, ""),
            new("GET", "/api/v1/admin/version", string.Join(",", new[] { Permissions.Admin.Dashboard }), false, ""),
            new("POST", "/api/v1/workspaces/{workspaceId:guid}/channels/{channelId:guid}/ai/summarize", string.Join(",", new[] { Permissions.Ai.Summarize }), false, ""),
            new("POST", "/api/v1/workspaces/{workspaceId:guid}/channels/{channelId:guid}/ai/suggest-reply", string.Join(",", new[] { Permissions.Ai.SuggestReply }), false, ""),
            new("POST", "/api/v1/workspaces/{workspaceId:guid}/channels/{channelId:guid}/messages/{messageId:guid}/attachments/{attachmentId:guid}/transcribe", string.Join(",", new[] { Permissions.Ai.Transcribe }), false, ""),
            new("POST", "/api/v1/dev/seed", "", true, "Development seed; AllowAnonymous lab-only"),
        ];

        var actual = endpoints.Select(endpoint => new EndpointContract(
            string.Join(",", endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods),
            endpoint.RoutePattern.RawText!,
            string.Join(",", endpoint.Metadata.GetOrderedMetadata<RequirePermissionAttribute>().Select(item => item.Permission)),
            endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null,
            ReadExemption(endpoint))).ToArray();

        Assert.Equal(expected, actual);
        Assert.All(endpoints, endpoint => Assert.NotEmpty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()));
    }

    private static string ReadExemption(RouteEndpoint endpoint)
    {
        var exemption = endpoint.Metadata.FirstOrDefault(item => item.GetType().Name == "PermissionGateExemptAttribute");
        return exemption?.GetType().GetProperty("Reason")?.GetValue(exemption) as string ?? "";
    }

    private sealed record EndpointContract(string Method, string Route, string Permissions, bool Anonymous, string Exemption);
}
