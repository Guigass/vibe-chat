using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Options;
using VibeChat.Administration;
using VibeChat.AI;
using VibeChat.BuildingBlocks;
using VibeChat.Files;
using VibeChat.Infrastructure;
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

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(8000, true)]
    [InlineData(8001, false)]
    public void Message_body_policies_enforce_utf16_code_unit_limit(int length, bool withinLimit)
    {
        var body = new string('a', length);
        MessageBodyPolicies.MeasureLength(body).Should().Be(length);
        MessageBodyPolicies.IsWithinLimit(body).Should().Be(withinLimit);
    }

    [Fact]
    public void Message_body_policies_normalize_trims_whitespace()
    {
        MessageBodyPolicies.Normalize("  hello  ").Should().Be("hello");
        MessageBodyPolicies.IsEmpty("   ").Should().BeTrue();
    }

    [Fact]
    public void Message_body_policies_unicode_uses_same_count_as_javascript_string_length()
    {
        const string emoji = "😀";
        emoji.Length.Should().Be(2);
        MessageBodyPolicies.MeasureLength(emoji).Should().Be(2);
    }

    [Fact]
    public void Attachment_policies_sanitize_file_name_and_validate_content_type()
    {
        AttachmentPolicies.SanitizeFileName(@"..\evil/report.pdf").Should().Be("report.pdf");
        AttachmentPolicies.SanitizeFileName("foto 1 (final).PNG").Should().Be("foto_1__final_.PNG");
        AttachmentPolicies.IsAllowedContentType("image/png", null).Should().BeTrue();
        AttachmentPolicies.IsAllowedContentType("application/x-msdownload", null).Should().BeFalse();
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(10, true)]
    [InlineData(11, false)]
    public void Attachment_policies_enforce_max_attachments_per_message(int count, bool withinLimit)
    {
        AttachmentPolicies.IsWithinAttachmentCount(count).Should().Be(withinLimit);
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
        var otherTenant = new TenantId(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));
        var user = new UserId(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));

        RateLimitKeys.SendMessage(tenant, user)
            .Should().Be($"t:{tenant.Value}:rl:send:{user.Value}");
        RateLimitKeys.Hub(tenant, user)
            .Should().Be($"t:{tenant.Value}:rl:hub:{user.Value}");
        RateLimitKeys.SendMessage(tenant, user).Should().NotBe(RateLimitKeys.Hub(tenant, user));
        RateLimitKeys.SendMessage(tenant, user)
            .Should().NotBe(RateLimitKeys.SendMessage(otherTenant, user));
    }

    [Fact]
    public void Redis_keys_are_tenant_prefixed()
    {
        // GAP-redis-keys — align with docs/security/multi-tenant.md
        var tenant = new TenantId(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        var otherTenant = new TenantId(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));
        var channel = new ChannelId(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
        var user = new UserId(Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"));

        RedisKeys.Typing(tenant, channel).Should().Be($"t:{tenant.Value}:typing:{channel.Value}");
        RedisKeys.PresenceStatus(tenant, user).Should().Be($"t:{tenant.Value}:presence:status:{user.Value}");
        RedisKeys.PresenceConnections(tenant, user).Should().Be($"t:{tenant.Value}:presence:conn:{user.Value}");
        RedisKeys.PresenceUsers(tenant).Should().Be($"t:{tenant.Value}:presence:users");

        RedisKeys.Typing(tenant, channel).Should().NotBe(RedisKeys.Typing(otherTenant, channel));
        RedisKeys.PresenceUsers(tenant).Should().NotBe(RedisKeys.PresenceUsers(otherTenant));
        RedisKeys.Typing(tenant, channel).Should().NotStartWith("typing:");
        RedisKeys.PresenceStatus(tenant, user).Should().NotStartWith("presence:");
    }

    [Fact]
    public void SignalR_hub_groups_are_tenant_namespaced()
    {
        // GAP-signalr-groups — align with docs/security/multi-tenant.md
        var tenant = new TenantId(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        var otherTenant = new TenantId(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));
        var channel = new ChannelId(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));

        ChatHub.ChannelGroup(tenant, channel)
            .Should().Be($"t:{tenant.Value}:c:{channel.Value}");
        ChatHub.TenantGroup(tenant)
            .Should().Be($"t:{tenant.Value}");

        ChatHub.ChannelGroup(tenant, channel)
            .Should().NotBe(ChatHub.ChannelGroup(otherTenant, channel));
        ChatHub.ChannelGroup(tenant, channel)
            .Should().NotStartWith("channel:");
        ChatHub.TenantGroup(tenant)
            .Should().NotStartWith("tenant:");
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
    public void Emoji_validator_accepts_unicode_emoji_and_rejects_text()
    {
        EmojiValidator.IsValid("👍").Should().BeTrue();
        EmojiValidator.IsValid("❤️").Should().BeTrue();
        EmojiValidator.IsValid("🚀").Should().BeTrue();
        EmojiValidator.IsValid("👨‍👩‍👧").Should().BeTrue();
        EmojiValidator.IsValid(":)").Should().BeFalse();
        EmojiValidator.IsValid(" ").Should().BeFalse();
        EmojiValidator.IsValid("hello").Should().BeFalse();
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
    public void Mention_tokens_parse_user_here_and_channel()
    {
        var userId = UserId.New();
        var body = $"Hi {MentionTokens.UserBodyToken(userId)} {MentionTokens.HereBodyToken} {MentionTokens.ChannelBodyToken}";
        var tokens = MentionTokens.ParseBody(body);
        tokens.Should().HaveCount(3);
        tokens.Should().Contain(x => x.Kind == MentionKind.User && x.UserId == userId);
        tokens.Should().Contain(x => x.Kind == MentionKind.Here);
        tokens.Should().Contain(x => x.Kind == MentionKind.Channel);
    }

    [Fact]
    public void Permission_catalog_grants_mention_all_to_member()
    {
        RolePermissionCatalog.For(Role.Member).Should().Contain(Permissions.Channel.MentionAll);
        RolePermissionCatalog.For(Role.Guest).Should().NotContain(Permissions.Channel.MentionAll);
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
    public void Attachment_policies_validate_audio_waveform()
    {
        AttachmentPolicies.IsValidWaveform([10, 50, 100]).Should().BeTrue();
        AttachmentPolicies.IsValidWaveform([101]).Should().BeFalse();
        AttachmentPolicies.NormalizeWaveform(Enumerable.Range(0, 200).Select(i => i % 100).ToArray())
            .Should().HaveCount(AttachmentPolicies.MaxWaveformPoints);
    }

    [Fact]
    public async Task Mock_ai_transcribes_without_requiring_external_provider()
    {
        var provider = new MockAiProvider();

        var response = await provider.CompleteAsync(
            new AiCompletionRequest("Transcribe the described audio attachment.", "Audio attachment voice.webm; duration 5s"),
            CancellationToken.None);

        response.Text.Should().Contain("Mock transcription");
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

    [Fact]
    public void Runtime_secret_protector_round_trips_and_uses_fresh_nonce()
    {
        var protector = CreateTestProtector();
        var tenantId = TenantId.New();
        var workspaceId = WorkspaceId.New();
        var now = DateTimeOffset.UtcNow;

        var first = protector.Protect(
            "sk-test-secret-key99",
            RuntimeSecretKinds.OpenRouterApiKey,
            tenantId,
            workspaceId,
            workspaceId.Value.ToString("D"),
            now);
        var second = protector.Protect(
            "sk-test-secret-key99",
            RuntimeSecretKinds.OpenRouterApiKey,
            tenantId,
            workspaceId,
            workspaceId.Value.ToString("D"),
            now);

        first.Nonce.Should().NotBeEquivalentTo(second.Nonce);
        first.Ciphertext.Should().NotBeEquivalentTo(second.Ciphertext);
        first.MaskSuffix.Should().Be("ey99");
        first.KeyVersion.Should().Be(1);

        var plain = protector.Unprotect(
            first,
            RuntimeSecretKinds.OpenRouterApiKey,
            tenantId,
            workspaceId,
            workspaceId.Value.ToString("D"));
        plain.Should().Be("sk-test-secret-key99");
    }

    [Fact]
    public void Runtime_secret_protector_fails_closed_on_aad_or_missing_key()
    {
        var protector = CreateTestProtector();
        var tenantId = TenantId.New();
        var workspaceId = WorkspaceId.New();
        var envelope = protector.Protect(
            "smtp-test-password42",
            RuntimeSecretKinds.SmtpPassword,
            tenantId,
            workspaceId: null,
            tenantId.Value.ToString("D"),
            DateTimeOffset.UtcNow);

        var actWrongAad = () => protector.Unprotect(
            envelope,
            RuntimeSecretKinds.OpenRouterApiKey,
            tenantId,
            workspaceId: null,
            tenantId.Value.ToString("D"));
        actWrongAad.Should().Throw<CryptographicException>();

        var tampered = new EncryptedSecretEnvelope
        {
            Ciphertext = envelope.Ciphertext!.ToArray(),
            Nonce = envelope.Nonce!.ToArray(),
            Tag = envelope.Tag!.ToArray(),
            KeyVersion = envelope.KeyVersion,
            FormatVersion = envelope.FormatVersion,
            MaskSuffix = envelope.MaskSuffix,
            RotatedAt = envelope.RotatedAt
        };
        tampered.Ciphertext![0] ^= 0xFF;
        var actTamper = () => protector.Unprotect(
            tampered,
            RuntimeSecretKinds.SmtpPassword,
            tenantId,
            workspaceId: null,
            tenantId.Value.ToString("D"));
        actTamper.Should().Throw<CryptographicException>();

        var missingKey = new RuntimeSecretProtector(Options.Create(new RuntimeSettingsOptions
        {
            DatabaseOverridesEnabled = true,
            Encryption = new RuntimeEncryptionOptions
            {
                ActiveKeyVersion = 9,
                Keys = new Dictionary<string, string>(StringComparer.Ordinal)
            }
        }));
        missingKey.IsEncryptionAvailable.Should().BeFalse();
        var actMissing = () => missingKey.Protect(
            "webhook-signing-secret-99",
            RuntimeSecretKinds.WebhookSigningSecret,
            tenantId,
            workspaceId: null,
            tenantId.Value.ToString("D"),
            DateTimeOffset.UtcNow);
        actMissing.Should().Throw<CryptographicException>();
    }

    private static RuntimeSecretProtector CreateTestProtector(int activeVersion = 1)
    {
        var key = Convert.ToBase64String(new byte[32]);
        return new RuntimeSecretProtector(Options.Create(new RuntimeSettingsOptions
        {
            DatabaseOverridesEnabled = true,
            Encryption = new RuntimeEncryptionOptions
            {
                ActiveKeyVersion = activeVersion,
                Keys = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [activeVersion.ToString()] = key
                }
            }
        }));
    }
}
