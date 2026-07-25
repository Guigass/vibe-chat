using FluentAssertions;
using VibeChat.Administration;
using VibeChat.AI;
using VibeChat.BuildingBlocks;
using VibeChat.Files;
using VibeChat.Integrations;
using VibeChat.Messaging;
using VibeChat.Notifications;
using VibeChat.Search;
using VibeChat.SharedKernel;

namespace VibeChat.UnitTests;

public sealed class BackendUnitTests
{
    [Fact]
    public void Message_idempotency_hash_is_stable_for_same_command()
    {
        var tenantId = TenantId.New();
        var userId = UserId.New();
        var channelId = ChannelId.New();
        var messageId = MessageId.New();
        var command = new SendMessageCommand(tenantId, userId, channelId, messageId, "idem-1", "hello", null, null);

        var first = MessageIdempotency.ComputeRequestHash(command);
        var second = MessageIdempotency.ComputeRequestHash(command);

        first.Should().Be(second);
        first.Should().HaveLength(64);
    }

    [Fact]
    public void Message_idempotency_hash_changes_when_body_changes()
    {
        var command = new SendMessageCommand(TenantId.New(), UserId.New(), ChannelId.New(), MessageId.New(), "idem-1", "hello", null, null);
        var changed = command with { Body = "hello again" };

        MessageIdempotency.ComputeRequestHash(command).Should().NotBe(MessageIdempotency.ComputeRequestHash(changed));
    }

    [Fact]
    public void Message_idempotency_hash_changes_when_attachments_change()
    {
        var baseCommand = new SendMessageCommand(TenantId.New(), UserId.New(), ChannelId.New(), MessageId.New(), "idem-1", "hello", null, null, [Guid.NewGuid()]);
        var changed = baseCommand with { AttachmentIds = [Guid.NewGuid()] };

        MessageIdempotency.ComputeRequestHash(baseCommand).Should().NotBe(MessageIdempotency.ComputeRequestHash(changed));
    }

    [Fact]
    public void Attachment_policies_sanitize_file_name_and_validate_content_type()
    {
        AttachmentPolicies.SanitizeFileName(@"..\evil/report.pdf").Should().Be("report.pdf");
        AttachmentPolicies.SanitizeFileName("foto 1 (final).PNG").Should().Be("foto_1__final_.PNG");
        AttachmentPolicies.IsAllowedContentType("image/png", null).Should().BeTrue();
        AttachmentPolicies.IsAllowedContentType("application/x-msdownload", null).Should().BeFalse();
    }

    [Fact]
    public void Permission_catalog_allows_member_to_upload_files()
    {
        RolePermissionCatalog.For(Role.Member).Should().Contain(Permissions.Files.Upload);
        RolePermissionCatalog.For(Role.Guest).Should().NotContain(Permissions.Files.Upload);
    }

    [Fact]
    public void Permission_catalog_allows_member_to_search_messages()
    {
        RolePermissionCatalog.For(Role.Member).Should().Contain(Permissions.Search.Messages);
        RolePermissionCatalog.For(Role.Guest).Should().NotContain(Permissions.Search.Messages);
    }

    [Fact]
    public void Search_policies_normalize_term_limit_and_preview()
    {
        SearchPolicies.NormalizeTerm("  hello  ").Should().Be("hello");
        SearchPolicies.NormalizeLimit(null).Should().Be(SearchPolicies.DefaultLimit);
        SearchPolicies.NormalizeLimit(0).Should().Be(1);
        SearchPolicies.NormalizeLimit(999).Should().Be(SearchPolicies.MaxLimit);
        SearchPolicies.BuildPreview(new string('a', 200)).Should().EndWith("…").And.HaveLength(SearchPolicies.PreviewLength + 1);
    }

    [Fact]
    public void Rate_limit_keys_are_tenant_and_user_scoped()
    {
        var tenant = new TenantId(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        var user = new UserId(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));

        RateLimitKeys.SendMessage(tenant, user).Should().Contain("rl:send:");
        RateLimitKeys.Hub(tenant, user).Should().Contain("rl:hub:");
        RateLimitKeys.SendMessage(tenant, user).Should().NotBe(RateLimitKeys.Hub(tenant, user));
    }

    [Fact]
    public void Permission_catalog_allows_member_to_send_but_not_view_admin_dashboard()
    {
        var permissions = RolePermissionCatalog.For(Role.Member);

        permissions.Should().Contain(Permissions.Message.Send);
        permissions.Should().Contain(Permissions.Message.React);
        permissions.Should().Contain(Permissions.Channel.Create);
        permissions.Should().Contain(Permissions.Ai.SuggestReply);
        permissions.Should().NotContain(Permissions.Admin.Dashboard);
    }

    [Fact]
    public void Reaction_emoji_allowlist_accepts_mvp_set()
    {
        ReactionEmojis.IsAllowed("👍").Should().BeTrue();
        ReactionEmojis.IsAllowed("❤️").Should().BeTrue();
        ReactionEmojis.IsAllowed("🚀").Should().BeFalse();
        ReactionEmojis.IsAllowed(" ").Should().BeFalse();
        RolePermissionCatalog.For(Role.Guest).Should().NotContain(Permissions.Message.React);
    }

    [Fact]
    public void Permission_catalog_denies_guest_channel_create()
    {
        RolePermissionCatalog.For(Role.Guest).Should().NotContain(Permissions.Channel.Create);
        RolePermissionCatalog.For(Role.Member).Should().Contain(Permissions.Channel.Create);
    }

    [Fact]
    public void Workspace_role_policies_block_self_elevation_and_guest()
    {
        WorkspaceRolePolicies.CanManageRoles(Role.WorkspaceOwner).Should().BeTrue();
        WorkspaceRolePolicies.CanManageRoles(Role.Admin).Should().BeTrue();
        WorkspaceRolePolicies.CanManageRoles(Role.Member).Should().BeFalse();

        WorkspaceRolePolicies.CanChangeMemberRole(Role.WorkspaceOwner, Role.Member, Role.Admin, isSelf: false)
            .Should().BeTrue();
        WorkspaceRolePolicies.CanChangeMemberRole(Role.Member, Role.Member, Role.Admin, isSelf: true)
            .Should().BeFalse();
        WorkspaceRolePolicies.CanChangeMemberRole(Role.WorkspaceOwner, Role.Member, Role.Guest, isSelf: false)
            .Should().BeFalse();
        WorkspaceRolePolicies.CanChangeMemberRole(Role.Admin, Role.WorkspaceOwner, Role.Member, isSelf: false)
            .Should().BeFalse();
        WorkspaceRolePolicies.IsAssignable(Role.Moderator).Should().BeTrue();
        WorkspaceRolePolicies.IsAssignable(Role.Guest).Should().BeFalse();
    }

    [Fact]
    public async Task Null_email_sender_is_disabled_by_default()
    {
        var sender = new NullEmailSender();
        sender.IsEnabled.Should().BeFalse();
        await sender.SendAsync(new EmailMessage("a@b.c", "subj", "body"), CancellationToken.None);
    }

    [Fact]
    public void Secret_masking_hides_clear_values()
    {
        SecretMasking.IsConfigured(null).Should().BeFalse();
        SecretMasking.IsConfigured("").Should().BeFalse();
        SecretMasking.IsConfigured("CHANGE_ME").Should().BeFalse();
        SecretMasking.Mask("ab").Should().Be("••••");
        SecretMasking.Mask("sk-test-secret-key99").Should().Be("••••ey99");
        SecretMasking.Mask("sk-test-secret-key99").Should().NotContain("sk-test");
    }

    [Fact]
    public void Webhook_signature_is_stable_hmac_sha256()
    {
        var body = """{"tenantId":"00000000-0000-0000-0000-000000000001","body":"hi"}""";
        var first = WebhookDelivery.ComputeSignature("super-secret", body);
        var second = WebhookDelivery.ComputeSignature("super-secret", body);

        first.Should().Be(second);
        first.Should().StartWith("sha256=");
        first.Should().HaveLength("sha256=".Length + 64);
        WebhookDelivery.ComputeSignature("other", body).Should().NotBe(first);
    }

    [Fact]
    public void Webhook_url_validation_allows_https_and_localhost_http()
    {
        WebhookDelivery.IsValidHttpsUrl("https://hooks.example.com/v1").Should().BeTrue();
        WebhookDelivery.IsValidHttpsUrl("http://localhost:9090/hook").Should().BeTrue();
        WebhookDelivery.IsValidHttpsUrl("http://127.0.0.1/hook").Should().BeTrue();
        WebhookDelivery.IsValidHttpsUrl("http://evil.example.com/hook").Should().BeFalse();
        WebhookDelivery.IsValidHttpsUrl("ftp://localhost/hook").Should().BeFalse();
        WebhookDelivery.IsValidHttpsUrl("not-a-url").Should().BeFalse();
    }

    [Fact]
    public void Webhook_settings_status_resolves_from_flags()
    {
        WebhooksSettingsStatus.Resolve(true, false, true).Should().Be(WebhooksSettingsStatus.Unconfigured);
        WebhooksSettingsStatus.Resolve(false, true, true).Should().Be(WebhooksSettingsStatus.Disabled);
        WebhooksSettingsStatus.Resolve(true, true, true).Should().Be(WebhooksSettingsStatus.Active);
    }

    [Fact]
    public void Message_sequences_are_orderable_per_conversation()
    {
        var channelId = ChannelId.New();
        var messages = new[]
        {
            new Message { ConversationId = channelId, Sequence = 3, Body = "c" },
            new Message { ConversationId = channelId, Sequence = 1, Body = "a" },
            new Message { ConversationId = channelId, Sequence = 2, Body = "b" }
        };

        messages.OrderBy(x => x.Sequence).Select(x => x.Body).Should().Equal("a", "b", "c");
    }

    [Fact]
    public async Task Mock_ai_summarizes_without_requiring_external_provider()
    {
        var provider = new MockAiProvider();

        var response = await provider.CompleteAsync(new AiCompletionRequest("summarize", "one\ntwo\nthree"), CancellationToken.None);

        response.Text.Should().StartWith("Mock summary:");
        response.Text.Should().NotContain("one");
        response.PromptTokens.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Mock_ai_suggests_reply_without_requiring_external_provider()
    {
        var provider = new MockAiProvider();

        var response = await provider.CompleteAsync(
            new AiCompletionRequest("Suggest one short, professional reply.", "one\ntwo\nthree"),
            CancellationToken.None);

        response.Text.Should().StartWith("Mock suggestion:");
        response.Text.Should().NotContain("one");
        response.PromptTokens.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Null_ai_provider_returns_disabled_without_external_calls()
    {
        var provider = new NullAiProvider();
        var response = await provider.CompleteAsync(new AiCompletionRequest("summarize", "secret"), CancellationToken.None);

        provider.Name.Should().Be("Null");
        response.Text.Should().Contain("disabled");
        response.PromptTokens.Should().Be(0);
    }

    [Fact]
    public void OpenRouter_extracts_chat_completion_content()
    {
        var json = """{"choices":[{"message":{"role":"assistant","content":"Hello summary"}}]}""";
        OpenRouterAiProvider.ExtractChatCompletionText(json).Should().Be("Hello summary");
    }
}
