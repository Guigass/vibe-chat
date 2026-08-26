using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
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
    public void Attachment_reference_policies_gate_blob_delete_on_zero_rows()
    {
        AttachmentReferencePolicies.CountAfterAdd(1).Should().Be(2);
        AttachmentReferencePolicies.CountAfterRelease(2).Should().Be(1);
        AttachmentReferencePolicies.CountAfterRelease(1).Should().Be(0);
        AttachmentReferencePolicies.CanDeleteBlob(1).Should().BeFalse();
        AttachmentReferencePolicies.CanDeleteBlob(0).Should().BeTrue();
    }

    [Fact]
    public void Forward_idempotency_hash_is_stable_for_same_targets()
    {
        var tenantId = TenantId.New();
        var userId = UserId.New();
        var workspaceId = WorkspaceId.New();
        var sourceId = MessageId.New();
        var a = ChannelId.New();
        var b = ChannelId.New();
        var command = new ForwardMessageCommand(
            tenantId,
            userId,
            workspaceId,
            sourceId,
            "idem-fwd",
            [b, a],
            "  hi  ");

        var normalized = command with
        {
            TargetChannelIds = [a, b],
            Comment = MessageBodyPolicies.Normalize(command.Comment)
        };
        MessageIdempotency.ComputeForwardRequestHash(normalized)
            .Should().Be(MessageIdempotency.ComputeForwardRequestHash(normalized with { TargetChannelIds = [b, a] }));
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
    public void Attachment_thumbnail_policies_detect_eligible_types_and_limits()
    {
        AttachmentPolicies.IsThumbnailEligible("image/png").Should().BeTrue();
        AttachmentPolicies.IsThumbnailEligible("application/pdf").Should().BeTrue();
        AttachmentPolicies.IsThumbnailEligible("text/plain").Should().BeFalse();
        AttachmentPolicies.IsWithinThumbnailInputLimits(100, 100).Should().BeTrue();
        AttachmentPolicies.IsWithinThumbnailInputLimits(9000, 100).Should().BeFalse();
        AttachmentPolicies.IsWithinThumbnailInputLimits(5000, 5001).Should().BeFalse();
        var key = AttachmentPolicies.BuildThumbnailKey(TenantId.New(), ChannelId.New(), Guid.NewGuid());
        key.Should().EndWith("/thumb.webp");
    }

    [Fact]
    public void Attachment_thumbnail_codec_resizes_image_to_max_edge()
    {
        using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(1200, 800);
        using var input = new MemoryStream();
        image.Save(input, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
        input.Position = 0;

        var result = AttachmentThumbnailCodec.FromImage(input);
        result.Success.Should().BeTrue();
        result.Width.Should().Be(1200);
        result.Height.Should().Be(800);
        result.WebpBytes.Should().NotBeNull();
        result.WebpBytes!.Length.Should().BeGreaterThan(0);

        using var thumb = SixLabors.ImageSharp.Image.Load(result.WebpBytes);
        Math.Max(thumb.Width, thumb.Height).Should().Be(AttachmentPolicies.ThumbnailMaxEdgePx);
    }

    [Fact]
    public void Attachment_thumbnail_codec_rejects_unsupported_content_type()
    {
        using var input = new MemoryStream([1, 2, 3, 4]);
        var result = AttachmentThumbnailCodec.FromContent(input, "text/plain");
        result.Success.Should().BeFalse();
        result.Error.Should().Be("UnsupportedContentType");
    }

    [Theory]
    [InlineData("see https://example.com/path?q=1 more", "https://example.com/path?q=1")]
    [InlineData("no link here", null)]
    [InlineData("ftp://example.com/x", null)]
    public void Link_preview_extracts_first_http_url(string body, string? expected)
    {
        LinkPreviewPolicies.ExtractFirstUrl(body).Should().Be(expected);
    }

    [Theory]
    [InlineData("file:///etc/passwd", null)]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("https://example.com/a", "https://example.com/a")]
    public void Link_preview_normalize_allows_only_http_https(string raw, string? expected)
    {
        LinkPreviewPolicies.NormalizeUrl(raw).Should().Be(expected);
    }

    [Fact]
    public void Link_preview_url_hash_is_stable_and_hex()
    {
        var hash = LinkPreviewPolicies.ComputeUrlHash("https://example.com/");
        hash.Should().HaveLength(64);
        hash.Should().MatchRegex("^[0-9a-f]{64}$");
        LinkPreviewPolicies.ComputeUrlHash("https://example.com/").Should().Be(hash);
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("10.0.0.1", true)]
    [InlineData("192.168.1.1", true)]
    [InlineData("169.254.169.254", true)]
    [InlineData("172.16.5.5", true)]
    [InlineData("8.8.8.8", false)]
    [InlineData("1.1.1.1", false)]
    public void Link_preview_blocks_private_and_metadata_ips(string ip, bool blocked)
    {
        var address = System.Net.IPAddress.Parse(ip);
        LinkPreviewPolicies.IsBlockedIp(address).Should().Be(blocked);
    }

    [Fact]
    public void Open_graph_parser_reads_og_tags_and_title_fallback()
    {
        var html = """
            <html><head>
            <meta property="og:title" content="Hello &amp; Co" />
            <meta property="og:description" content="Desc" />
            <meta property="og:image" content="/img.png" />
            <meta property="og:site_name" content="Site" />
            <title>Ignored</title>
            </head></html>
            """;
        var meta = OpenGraphParser.Parse(html, new Uri("https://example.com/page"));
        meta.Title.Should().Be("Hello & Co");
        meta.Description.Should().Be("Desc");
        meta.SiteName.Should().Be("Site");
        meta.ImageUrl.Should().Be("https://example.com/img.png");
    }

    [Fact]
    public void Open_graph_parser_reads_twitter_image_fallback()
    {
        var html = """
            <html><head>
            <meta name="twitter:title" content="Tweet Title" />
            <meta name="twitter:image" content="//cdn.example.com/pic.jpg" />
            </head></html>
            """;
        var meta = OpenGraphParser.Parse(html, new Uri("https://example.com/page"));
        meta.Title.Should().Be("Tweet Title");
        meta.ImageUrl.Should().Be("https://cdn.example.com/pic.jpg");
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
        permissions.Should().Contain(Permissions.Message.Pin);
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
    public void Mention_tokens_format_plain_text_uses_display_names()
    {
        var userId = UserId.New();
        var body = $"hey {MentionTokens.UserBodyToken(userId)} {MentionTokens.HereBodyToken}";
        var names = new Dictionary<UserId, string> { [userId] = "Bob" };
        MentionTokens.FormatPlainText(body, names).Should().Be("hey @Bob @aqui");
        MentionTokens.FormatPlainText(MentionTokens.UserBodyToken(userId), null)
            .Should().Be("@usuário");
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
        AttachmentPolicies.IsAllowedVideoContentType("video/mp4").Should().BeTrue();
        AttachmentPolicies.IsAllowedVideoContentType("video/webm").Should().BeTrue();
        AttachmentPolicies.IsAllowedVideoContentType("video/quicktime").Should().BeFalse();
        AttachmentPolicies.DefaultVideoMaxSizeBytes.Should().Be(25 * 1024 * 1024);
        AttachmentPolicies.DefaultVideoMaxDurationMs.Should().Be(60_000);
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

    [Fact]
    public void Push_should_notify_dm_and_mentions_but_not_author_or_plain_channel()
    {
        const NotificationLevel level = NotificationLevel.MentionsAndDms;

        PushDispatchPolicies.ShouldNotifyForLevel(level, isDirect: true, isMentioned: true, isAuthor: false).Should().BeTrue();
        PushDispatchPolicies.ShouldNotifyForLevel(level, isDirect: true, isMentioned: false, isAuthor: true).Should().BeFalse();
        PushDispatchPolicies.ShouldNotifyForLevel(level, isDirect: false, isMentioned: true, isAuthor: false).Should().BeTrue();
        PushDispatchPolicies.ShouldNotifyForLevel(level, isDirect: false, isMentioned: false, isAuthor: false).Should().BeFalse();
    }

    [Fact]
    public void Push_cursor_suppresses_when_already_read()
    {
        PushDispatchPolicies.IsSuppressedByCursor(10, 10).Should().BeTrue();
        PushDispatchPolicies.IsSuppressedByCursor(11, 10).Should().BeTrue();
        PushDispatchPolicies.IsSuppressedByCursor(9, 10).Should().BeFalse();
        PushDispatchPolicies.IsSuppressedByCursor(null, 10).Should().BeFalse();
    }

    [Fact]
    public void Push_enabled_pref_defaults_true_unless_explicitly_false()
    {
        PushDispatchPolicies.IsPushEnabled(null).Should().BeTrue();
        PushDispatchPolicies.IsPushEnabled(true).Should().BeTrue();
        PushDispatchPolicies.IsPushEnabled(false).Should().BeFalse();
    }

    [Theory]
    // level, isDirect, isMentioned, isAuthor, expected
    [InlineData(NotificationLevel.None, true, true, false, false)]
    [InlineData(NotificationLevel.None, false, true, false, false)]
    [InlineData(NotificationLevel.MentionsAndDms, true, false, false, true)]
    [InlineData(NotificationLevel.MentionsAndDms, false, true, false, true)]
    [InlineData(NotificationLevel.MentionsAndDms, false, false, false, false)]
    [InlineData(NotificationLevel.All, false, false, false, true)]
    [InlineData(NotificationLevel.All, true, false, false, true)]
    [InlineData(NotificationLevel.All, false, false, true, false)]
    public void ShouldNotifyForLevel_matches_decision_matrix(
        NotificationLevel level, bool isDirect, bool isMentioned, bool isAuthor, bool expected)
    {
        PushDispatchPolicies.ShouldNotifyForLevel(level, isDirect, isMentioned, isAuthor).Should().Be(expected);
    }

    [Fact]
    public void ResolveEffectiveLevel_falls_back_to_global_when_channel_override_expired()
    {
        var now = DateTimeOffset.Parse("2026-08-26T12:00:00Z");

        PushDispatchPolicies.ResolveEffectiveLevel(NotificationLevel.MentionsAndDms, null, now)
            .Should().Be(NotificationLevel.MentionsAndDms);

        PushDispatchPolicies.ResolveEffectiveLevel(
                NotificationLevel.MentionsAndDms,
                (NotificationLevel.None, now.AddHours(1)),
                now)
            .Should().Be(NotificationLevel.None, "mute has not expired yet");

        PushDispatchPolicies.ResolveEffectiveLevel(
                NotificationLevel.MentionsAndDms,
                (NotificationLevel.None, now.AddHours(-1)),
                now)
            .Should().Be(NotificationLevel.MentionsAndDms, "mute expired — falls back to global");

        PushDispatchPolicies.ResolveEffectiveLevel(
                NotificationLevel.MentionsAndDms,
                (NotificationLevel.All, null),
                now)
            .Should().Be(NotificationLevel.All, "indefinite override never expires");
    }

    [Fact]
    public void IsWithinDnd_handles_overnight_window_and_day_mask_in_user_timezone()
    {
        const string tz = "America/Sao_Paulo";
        var start = new TimeOnly(20, 0);
        var end = new TimeOnly(8, 0);

        // 2026-08-26 22:00 UTC-3 (America/Sao_Paulo has no DST) -> 19:00 local, before the window.
        PushDispatchPolicies.IsWithinDnd(true, start, end, dndDays: 0, tz, DateTimeOffset.Parse("2026-08-26T22:00:00Z"))
            .Should().BeFalse();

        // 2026-08-27 01:00 UTC -> 22:00 local same day, inside the overnight window.
        PushDispatchPolicies.IsWithinDnd(true, start, end, dndDays: 0, tz, DateTimeOffset.Parse("2026-08-27T01:00:00Z"))
            .Should().BeTrue();

        // 2026-08-27 09:00 UTC -> 06:00 local, inside the window (wraps past midnight).
        PushDispatchPolicies.IsWithinDnd(true, start, end, dndDays: 0, tz, DateTimeOffset.Parse("2026-08-27T09:00:00Z"))
            .Should().BeTrue();

        // 2026-08-27 13:00 UTC -> 10:00 local, outside the window.
        PushDispatchPolicies.IsWithinDnd(true, start, end, dndDays: 0, tz, DateTimeOffset.Parse("2026-08-27T13:00:00Z"))
            .Should().BeFalse();

        PushDispatchPolicies.IsWithinDnd(false, start, end, dndDays: 0, tz, DateTimeOffset.Parse("2026-08-27T01:00:00Z"))
            .Should().BeFalse("DND disabled");

        PushDispatchPolicies.IsWithinDnd(true, start, end, dndDays: 0, timeZoneId: null, DateTimeOffset.Parse("2026-08-27T01:00:00Z"))
            .Should().BeFalse("missing time zone fails open");

        PushDispatchPolicies.IsWithinDnd(true, start, end, dndDays: 0, "Not/AZone", DateTimeOffset.Parse("2026-08-27T01:00:00Z"))
            .Should().BeFalse("invalid time zone fails open");

        // 2026-08-27 01:00 UTC is a Wednesday in America/Sao_Paulo (bit 1<<3 = 8); mask only includes Monday (1<<1 = 2).
        PushDispatchPolicies.IsWithinDnd(true, start, end, dndDays: 2, tz, DateTimeOffset.Parse("2026-08-27T01:00:00Z"))
            .Should().BeFalse("day not in mask");
    }

    [Fact]
    public void IsPriorityBypass_only_bypasses_for_dm_from_marked_contact()
    {
        var priority = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        var priorityContacts = new[] { priority };

        PushDispatchPolicies.IsPriorityBypass(isDirect: true, priority, priorityContacts).Should().BeTrue();
        PushDispatchPolicies.IsPriorityBypass(isDirect: false, priority, priorityContacts).Should().BeFalse();
        PushDispatchPolicies.IsPriorityBypass(isDirect: true, stranger, priorityContacts).Should().BeFalse();
    }

    [Fact]
    public void Push_preview_truncates_and_payload_is_ngsw_minimal()
    {
        var longBody = new string('x', 120);
        var preview = PushDispatchPolicies.TruncatePreview(longBody);
        preview.Length.Should().BeLessThanOrEqualTo(PushDispatchPolicies.PreviewMaxChars + 1);
        preview.Should().EndWith("…");

        var channelId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var messageId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var json = PushDispatchPolicies.BuildNgswPayload(
            "Alice",
            false,
            "geral",
            preview,
            channelId,
            messageId,
            42);
        json.Should().NotContain("p256dh");
        json.Should().NotContain("auth");

        using var doc = JsonDocument.Parse(json);
        var notification = doc.RootElement.GetProperty("notification");
        notification.GetProperty("title").GetString().Should().Be("Alice · #geral");
        notification.GetProperty("body").GetString().Should().Be(preview);
        notification.GetProperty("tag").GetString().Should().Be(messageId.ToString("D"));
        var data = notification.GetProperty("data");
        data.GetProperty("channelId").GetGuid().Should().Be(channelId);
        data.GetProperty("messageId").GetGuid().Should().Be(messageId);
        data.GetProperty("seq").GetInt64().Should().Be(42);
        data.GetProperty("onActionClick").GetProperty("default").GetProperty("url").GetString()
            .Should().Be($"/app?channel={channelId:D}&message={messageId:D}&seq=42");
    }

    [Fact]
    public void Push_title_hides_internal_ids_and_dm_channel_name()
    {
        PushDispatchPolicies.NotificationTitle(false, "Alice", "geral")
            .Should().Be("Alice · #geral");
        PushDispatchPolicies.NotificationTitle(
                true,
                "Alice",
                "dm:44444444-4444-4444-4444-444444444444:55555555-5555-5555-5555-555555555555")
            .Should().Be("Alice");
        PushDispatchPolicies.NotificationTitle(true, Guid.NewGuid().ToString(), "dm:x:y")
            .Should().Be("Mensagem direta");
        PushDispatchPolicies.ChannelLabel(true, "dm:aaaa:bbbb")
            .Should().Be("mensagem direta");
    }

    [Fact]
    public void Lab_demo_keyring_is_available_and_change_me_is_not()
    {
        var demo = new RuntimeSecretProtector(Options.Create(new RuntimeSettingsOptions
        {
            DatabaseOverridesEnabled = true,
            Encryption = new RuntimeEncryptionOptions
            {
                ActiveKeyVersion = 1,
                Keys = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["1"] = ProcessSettingsDefaults.LabDemoKeyBase64
                }
            }
        }));
        demo.IsEncryptionAvailable.Should().BeTrue();

        var changeMe = new RuntimeSecretProtector(Options.Create(new RuntimeSettingsOptions
        {
            DatabaseOverridesEnabled = true,
            Encryption = new RuntimeEncryptionOptions
            {
                ActiveKeyVersion = 1,
                Keys = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["1"] = "CHANGE_ME_base64_32_bytes"
                }
            }
        }));
        changeMe.IsEncryptionAvailable.Should().BeFalse();
    }

    [Fact]
    public void OpenRouter_base_url_rejects_http_and_private_hosts()
    {
        OpenRouterBaseUrlPolicies.IsValid("https://openrouter.ai/api/v1").Should().BeTrue();
        OpenRouterBaseUrlPolicies.IsValid("http://openrouter.ai/api/v1").Should().BeFalse();
        OpenRouterBaseUrlPolicies.IsValid("https://127.0.0.1/api").Should().BeFalse();
        OpenRouterBaseUrlPolicies.IsValid("https://10.0.0.8/api").Should().BeFalse();
        OpenRouterBaseUrlPolicies.IsValid("https://localhost/api").Should().BeFalse();
        OpenRouterBaseUrlPolicies.IsValid("not-a-url").Should().BeFalse();
    }

    [Fact]
    public void Secret_masking_treats_change_me_vapid_as_unconfigured()
    {
        SecretMasking.IsConfigured("CHANGE_ME").Should().BeFalse();
        SecretMasking.IsConfigured("Breal-public-key").Should().BeTrue();
    }

    [Fact]
    public async Task Null_push_sender_is_disabled_and_vapid_generator_creates_url_safe_keys()
    {
        var sender = new NullPushSender();
        sender.IsEnabled.Should().BeFalse();
        var result = await sender.SendAsync(
            new PushDeliveryRequest("https://example.test/push", "p256", "auth", "{}"),
            CancellationToken.None);
        result.Status.Should().Be(PushSendStatus.Delivered);

        var pair = VapidKeyGenerator.Create();
        pair.PublicKey.Should().NotBeNullOrWhiteSpace();
        pair.PrivateKey.Should().NotBeNullOrWhiteSpace();
        pair.PublicKey.Should().NotBe(pair.PrivateKey);
        pair.PublicKey.Should().NotContain("+");
        pair.PublicKey.Should().NotContain("/");
        pair.PublicKey.Should().NotContain("=");
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
