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
            new("PUT", "/api/v1/users/{userId:guid}/appearance", "", false, "caller-only visual preference (B-185)"),
            new("PUT", "/api/v1/me/profile", "", false, "caller-only public profile (B-167)"),
            new("POST", "/api/v1/me/profile/avatar", "", false, "caller-only avatar upload (B-167)"),
            new("DELETE", "/api/v1/me/profile/avatar", "", false, "caller-only avatar delete (B-167)"),
            new("GET", "/api/v1/workspaces/{workspaceId:guid}/members/{userId:guid}/profile", "", false, ""),
            new("GET", "/api/v1/workspaces/{workspaceId:guid}/members/{userId:guid}/profile/avatar", "", false, ""),
            new("GET", "/api/v1/workspaces", "", false, ""),
            new("GET", "/api/v1/workspaces/{workspaceId:guid}/channels", "", false, ""),
            new("GET", "/api/v1/workspaces/{workspaceId:guid}/channels/unread", "", false, ""),
            new("GET", "/api/v1/workspaces/{workspaceId:guid}/spaces", "", false, ""),
            new("POST", "/api/v1/workspaces/{workspaceId:guid}/spaces", string.Join(",", new[] { Permissions.Channel.Create }), false, ""),
            new("GET", "/api/v1/workspaces/{workspaceId:guid}/members", "", false, ""),
            new("GET", "/api/v1/workspaces/{workspaceId:guid}/contact-groups", "", false, ""),
            new("POST", "/api/v1/workspaces/{workspaceId:guid}/contact-groups", "", false, "department requires workspace.admin; personal is owner-only (B-166)"),
            new("PATCH", "/api/v1/workspaces/{workspaceId:guid}/contact-groups/{groupId:guid}", "", false, "department requires workspace.admin; personal is owner-only (B-166)"),
            new("DELETE", "/api/v1/workspaces/{workspaceId:guid}/contact-groups/{groupId:guid}", "", false, "department requires workspace.admin; personal is owner-only (B-166)"),
            new("PUT", "/api/v1/workspaces/{workspaceId:guid}/contact-groups/{groupId:guid}/members", "", false, "department requires workspace.admin; personal is owner-only (B-166)"),
            new("GET", "/api/v1/workspaces/{workspaceId:guid}/contacts", "", false, ""),
            new("GET", "/api/v1/workspaces/{workspaceId:guid}/channels/{channelId:guid}/members", "", false, ""),
            new("POST", "/api/v1/workspaces/{workspaceId:guid}/channels/{channelId:guid}/members", "", false, "channel creator or channel.manage (B-186)"),
            new("DELETE", "/api/v1/workspaces/{workspaceId:guid}/channels/{channelId:guid}/members/me", "", false, "membership-only channel leave (B-186)"),
            new("DELETE", "/api/v1/workspaces/{workspaceId:guid}/channels/{channelId:guid}/members/{userId:guid}", "", false, "channel creator or channel.manage (B-186)"),
            new("GET", "/api/v1/workspaces/{workspaceId:guid}/roles", string.Join(",", new[] { Permissions.Workspace.Admin }), false, ""),
            new("POST", "/api/v1/workspaces/{workspaceId:guid}/members", string.Join(",", new[] { Permissions.Workspace.Admin }), false, ""),
            new("PUT", "/api/v1/workspaces/{workspaceId:guid}/members/{userId:guid}/role", string.Join(",", new[] { Permissions.Workspace.Admin }), false, ""),
            new("POST", "/api/v1/workspaces/{workspaceId:guid}/channels/{channelId:guid}/invites", string.Join(",", new[] { Permissions.Workspace.Admin }), false, ""),
            new("GET", "/api/v1/workspaces/{workspaceId:guid}/channels/{channelId:guid}/invites", string.Join(",", new[] { Permissions.Workspace.Admin }), false, ""),
            new("DELETE", "/api/v1/invites/{inviteId:guid}", string.Join(",", new[] { Permissions.Workspace.Admin }), false, ""),
            new("POST", "/api/v1/invites/{token}/accept", "", false, "authenticated accept of hashed channel invite (B-040)"),
            new("GET", "/api/v1/workspaces/{workspaceId:guid}/presence", "", false, ""),
            new("GET", "/api/v1/me/status", string.Join(",", new[] { Permissions.Message.Read }), false, ""),
            new("PUT", "/api/v1/me/status", string.Join(",", new[] { Permissions.Message.Read }), false, ""),
            new("DELETE", "/api/v1/me/status", string.Join(",", new[] { Permissions.Message.Read }), false, ""),
            new("GET", "/api/v1/workspaces/{workspaceId:guid}/availability", string.Join(",", new[] { Permissions.Message.Read }), false, ""),
            new("DELETE", "/api/v1/workspaces/{workspaceId:guid}/members/{userId:guid}/status", string.Join(",", new[] { Permissions.Workspace.Admin }), false, ""),
            new("POST", "/api/v1/workspaces/{workspaceId:guid}/members/{userId:guid}/status/report", string.Join(",", new[] { Permissions.Message.Read }), false, ""),
            new("GET", "/api/v1/me/availability/calendar", string.Join(",", new[] { Permissions.Message.Read }), false, ""),
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
            new("POST", "/api/v1/channels/{channelId:guid}/messages/{messageId:guid}/acknowledgements", string.Join(",", new[] { Permissions.Announcement.Acknowledge }), false, ""),
            new("GET", "/api/v1/channels/{channelId:guid}/messages/{messageId:guid}/acknowledgements", "", false, "announcement.publish or workspace.admin (B-112)"),
            new("POST", "/api/v1/channels/{channelId:guid}/messages/{messageId:guid}/acknowledgements/close", string.Join(",", new[] { Permissions.Announcement.Publish }), false, ""),
            new("GET", "/api/v1/workspaces/{workspaceId:guid}/announcements/pending", string.Join(",", new[] { Permissions.Message.Read }), false, ""),
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
            new("GET", "/api/v1/channels/{channelId:guid}/messaging-policy", string.Join(",", new[] { Permissions.Message.Read }), false, ""),
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
            new("GET", "/api/v1/channels/{channelId:guid}/messages/{messageId:guid}/history", string.Join(",", new[] { Permissions.Message.Read }), false, ""),
            new("GET", "/api/v1/channels/{channelId:guid}/messages/{messageId:guid}/move", string.Join(",", new[] { Permissions.Message.Read }), false, ""),
            new("POST", "/api/v1/channels/{channelId:guid}/messages/{messageId:guid}/move", string.Join(",", new[] { Permissions.Message.Move }), false, ""),
            new("POST", "/api/v1/channels/{channelId:guid}/scheduled-messages", string.Join(",", new[] { Permissions.Message.Send }), false, ""),
            new("PATCH", "/api/v1/workspaces/{workspaceId:guid}/scheduled-messages/{scheduledMessageId:guid}", string.Join(",", new[] { Permissions.Message.Send }), false, ""),
            new("DELETE", "/api/v1/workspaces/{workspaceId:guid}/scheduled-messages/{scheduledMessageId:guid}", string.Join(",", new[] { Permissions.Message.Send }), false, ""),
            new("GET", "/api/v1/workspaces/{workspaceId:guid}/schedule", string.Join(",", new[] { Permissions.Message.Read }), false, ""),
            new("POST", "/api/v1/workspaces/{workspaceId:guid}/reminders", string.Join(",", new[] { Permissions.Message.Read }), false, ""),
            new("PATCH", "/api/v1/workspaces/{workspaceId:guid}/reminders/{reminderId:guid}", string.Join(",", new[] { Permissions.Message.Read }), false, ""),
            new("DELETE", "/api/v1/workspaces/{workspaceId:guid}/reminders/{reminderId:guid}", string.Join(",", new[] { Permissions.Message.Read }), false, ""),
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
            new("POST", "/api/v1/admin/webhooks", string.Join(",", new[] { Permissions.Workspace.Admin }), false, ""),
            new("PUT", "/api/v1/admin/webhooks/{endpointId:guid}", string.Join(",", new[] { Permissions.Workspace.Admin }), false, ""),
            new("DELETE", "/api/v1/admin/webhooks/{endpointId:guid}", string.Join(",", new[] { Permissions.Workspace.Admin }), false, ""),
            new("POST", "/api/v1/admin/webhooks/{endpointId:guid}/rotate", string.Join(",", new[] { Permissions.Workspace.Admin }), false, ""),
            new("POST", "/api/v1/admin/webhooks/{endpointId:guid}/test", string.Join(",", new[] { Permissions.Workspace.Admin }), false, ""),
            new("POST", "/api/v1/admin/settings/credentials/vapid/rotate", string.Join(",", new[] { Permissions.Workspace.Admin }), false, ""),
            new("POST", "/api/v1/admin/settings/encryption/reencrypt", string.Join(",", new[] { Permissions.Workspace.Admin }), false, ""),
            new("GET", "/api/v1/admin/workspaces/{workspaceId:guid}/export", string.Join(",", new[] { Permissions.Workspace.Admin }), false, ""),
            new("GET", "/api/v1/admin/health-summary", string.Join(",", new[] { Permissions.Admin.Dashboard }), false, ""),
            new("GET", "/api/v1/admin/version", string.Join(",", new[] { Permissions.Admin.Dashboard }), false, ""),
            new("POST", "/api/v1/workspaces/{workspaceId:guid}/channels/{channelId:guid}/ai/summarize", string.Join(",", new[] { Permissions.Ai.Summarize }), false, ""),
            new("POST", "/api/v1/workspaces/{workspaceId:guid}/channels/{channelId:guid}/ai/suggest-reply", string.Join(",", new[] { Permissions.Ai.SuggestReply }), false, ""),
            new("POST", "/api/v1/workspaces/{workspaceId:guid}/channels/{channelId:guid}/messages/{messageId:guid}/attachments/{attachmentId:guid}/transcribe", string.Join(",", new[] { Permissions.Ai.Transcribe }), false, ""),
            new("GET", "/api/v1/admin/workspaces/{workspaceId:guid}/bots", string.Join(",", new[] { Permissions.Workspace.Admin }), false, ""),
            new("POST", "/api/v1/admin/workspaces/{workspaceId:guid}/bots", string.Join(",", new[] { Permissions.Workspace.Admin }), false, ""),
            new("PUT", "/api/v1/admin/workspaces/{workspaceId:guid}/bots/{botId:guid}", string.Join(",", new[] { Permissions.Workspace.Admin }), false, ""),
            new("POST", "/api/v1/admin/workspaces/{workspaceId:guid}/bots/{botId:guid}/rotate", string.Join(",", new[] { Permissions.Workspace.Admin }), false, ""),
            new("POST", "/api/v1/admin/workspaces/{workspaceId:guid}/bots/{botId:guid}/revoke", string.Join(",", new[] { Permissions.Workspace.Admin }), false, ""),
            new("POST", "/api/v1/integrations/v1/channels/{channelId:guid}/messages", "", true, "integration token auth (B-109)"),
            new("POST", "/api/v1/integrations/v1/dms", "", true, "integration token auth (B-109)"),
            new("GET", "/api/v1/admin/workspaces/{workspaceId:guid}/plugins", string.Join(",", new[] { Permissions.Workspace.Admin }), false, ""),
            new("POST", "/api/v1/admin/workspaces/{workspaceId:guid}/plugins", string.Join(",", new[] { Permissions.Workspace.Admin }), false, ""),
            new("PATCH", "/api/v1/admin/workspaces/{workspaceId:guid}/plugins/{installedId:guid}", string.Join(",", new[] { Permissions.Workspace.Admin }), false, ""),
            new("DELETE", "/api/v1/admin/workspaces/{workspaceId:guid}/plugins/{installedId:guid}", string.Join(",", new[] { Permissions.Workspace.Admin }), false, ""),
            new("POST", "/api/v1/admin/workspaces/{workspaceId:guid}/plugins/{installedId:guid}/rotate", string.Join(",", new[] { Permissions.Workspace.Admin }), false, ""),
            new("GET", "/api/v1/admin/workspaces/{workspaceId:guid}/templates", string.Join(",", new[] { Permissions.Workspace.Admin }), false, ""),
            new("POST", "/api/v1/admin/workspaces/{workspaceId:guid}/templates/validate", string.Join(",", new[] { Permissions.Workspace.Admin }), false, ""),
            new("POST", "/api/v1/admin/workspaces/{workspaceId:guid}/templates/preview", string.Join(",", new[] { Permissions.Workspace.Admin }), false, ""),
            new("POST", "/api/v1/admin/workspaces/{workspaceId:guid}/templates/apply", string.Join(",", new[] { Permissions.Workspace.Admin }), false, ""),
            new("POST", "/api/v1/admin/workspaces/{workspaceId:guid}/templates/import", string.Join(",", new[] { Permissions.Workspace.Admin }), false, ""),
            new("GET", "/api/v1/admin/workspaces/{workspaceId:guid}/templates/export", string.Join(",", new[] { Permissions.Workspace.Admin }), false, ""),
            new("GET", "/api/v1/admin/workspaces/{workspaceId:guid}/templates/{templateId}/export", string.Join(",", new[] { Permissions.Workspace.Admin }), false, ""),
            new("GET", "/api/v1/admin/workspaces/{workspaceId:guid}/onboarding", string.Join(",", new[] { Permissions.Workspace.Admin }), false, ""),
            new("PUT", "/api/v1/admin/workspaces/{workspaceId:guid}/onboarding", string.Join(",", new[] { Permissions.Workspace.Admin }), false, ""),
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
