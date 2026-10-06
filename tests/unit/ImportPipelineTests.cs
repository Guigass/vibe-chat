using System.Text;
using System.Text.Json;
using FluentAssertions;
using VibeChat.Administration;
using VibeChat.BuildingBlocks;
using VibeChat.SharedKernel;

namespace VibeChat.UnitTests;

public sealed class ImportPipelineTests
{
    [Fact]
    public void Canonical_import_preserves_thread_time_and_ignores_tenant_claim()
    {
        const string json = """
            {
              "format": "vibechat.import.v1",
              "tenantId": "11111111-1111-1111-1111-111111111111",
              "source": { "system": "vibechat", "adapterVersion": "1" },
              "principals": [{ "externalId": "U1", "displayName": "Ada", "role": "Member" }],
              "spaces": [{ "externalId": "S1", "name": "Eng" }],
              "channels": [{ "externalId": "C1", "spaceExternalId": "S1", "name": "geral", "kind": "public" }],
              "threads": [{ "externalId": "T1", "channelExternalId": "C1", "rootMessageExternalId": "M1" }],
              "messages": [
                { "externalId": "M1", "channelExternalId": "C1", "authorExternalId": "U1", "body": "raiz", "createdAt": "2020-01-02T03:04:05Z" },
                { "externalId": "M2", "channelExternalId": "C1", "threadExternalId": "T1", "authorExternalId": "U1", "body": "resposta", "createdAt": "2020-01-02T03:05:05Z" }
              ],
              "attachments": []
            }
            """;

        var parsed = ImportAdapters.Parse("vibechat", json);
        parsed.Ok.Should().BeTrue();
        var inspected = ImportPolicies.Inspect(parsed.Document!);
        inspected.Ok.Should().BeTrue();
        inspected.Document!.TenantClaimIgnored.Should().BeTrue();
        inspected.Document.Messages[0].CreatedAt.Should().Be(DateTimeOffset.Parse("2020-01-02T03:04:05Z"));
        inspected.Report.Messages.Should().Be(2);
        inspected.Report.Warnings.Should().Contain("TenantIgnored");
        JsonSerializer.Serialize(inspected.Report).Should().NotContain("raiz");
    }

    [Fact]
    public void Forbidden_role_fails_closed()
    {
        var parsed = ImportAdapters.Parse("vibechat", """
            {
              "format": "vibechat.import.v1",
              "principals": [{ "externalId": "U1", "displayName": "Owner", "role": "WorkspaceOwner" }],
              "channels": [],
              "messages": []
            }
            """);
        ImportPolicies.Inspect(parsed.Document!).Error.Should().Be(ImportErrors.RoleForbidden);
    }

    [Fact]
    public void Slack_owner_fails_and_member_thread_is_preserved()
    {
        var owner = ImportAdapters.Parse("slack", """
            { "users": [{ "id": "U0", "real_name": "Owner", "is_owner": true }], "channels": [] }
            """);
        ImportPolicies.Inspect(owner.Document!).Error.Should().Be(ImportErrors.RoleForbidden);

        var parsed = ImportAdapters.Parse("slack", """
            {
              "users": [{ "id": "U1", "real_name": "Ada", "profile": { "display_name": "Ada" } }],
              "channels": [{
                "id": "C1",
                "name": "geral",
                "messages": [
                  { "ts": "1577934245.000100", "user": "U1", "text": "raiz", "thread_ts": "1577934245.000100" },
                  { "ts": "1577934300.000100", "user": "U1", "text": "resposta", "thread_ts": "1577934245.000100" }
                ]
              }]
            }
            """);
        var inspected = ImportPolicies.Inspect(parsed.Document!);
        inspected.Ok.Should().BeTrue();
        inspected.Document!.Principals.Single().Role.Should().Be("Member");
        inspected.Document.Messages.Should().HaveCount(2);
        inspected.Document.Messages.Single(x => x.ExternalId == "1577934300.000100").ThreadExternalId.Should().Be("1577934245.000100");
        inspected.Document.Messages.Single(x => x.ExternalId == "1577934245.000100").ThreadExternalId.Should().BeNull();
    }

    [Fact]
    public void Mattermost_and_discord_adapters_produce_canonical_messages()
    {
        var mattermost = ImportAdapters.Parse("mattermost", """
            {
              "users": [{ "id": "u1", "username": "ada", "roles": "system_user" }],
              "channels": [{
                "id": "c1", "name": "town-square", "team_name": "eng",
                "posts": [
                  { "id": "p1", "user_id": "u1", "message": "oi", "create_at": 1577934245000, "root_id": "" },
                  { "id": "p2", "user_id": "u1", "message": "thread", "create_at": 1577934300000, "root_id": "p1" }
                ]
              }]
            }
            """);
        var inspected = ImportPolicies.Inspect(mattermost.Document!);
        inspected.Ok.Should().BeTrue();
        inspected.Document!.Spaces.Single().Name.Should().Be("eng");
        inspected.Document.Messages.Should().HaveCount(2);

        var discord = ImportAdapters.Parse("discord", """
            {
              "guild": { "name": "Eng" },
              "channels": [{
                "id": "c1", "name": "geral",
                "messages": [{
                  "id": "m1",
                  "content": "oi",
                  "timestamp": "2020-01-02T03:04:05.000Z",
                  "author": { "id": "u1", "username": "ada" }
                }]
              }]
            }
            """);
        ImportPolicies.Inspect(discord.Document!).Document!.Messages.Single().Body.Should().Be("oi");
    }

    [Fact]
    public void Truncated_unknown_duplicate_and_ambiguous_documents_fail()
    {
        ImportAdapters.Parse("vibechat", "{ \"principals\": [").Error.Should().Be(ImportErrors.DocumentInvalid);
        ImportAdapters.Parse("nope", "{}").Error.Should().Be(ImportErrors.SchemaUnknown);
        ImportAdapters.Parse("vibechat", "{ \"format\": \"other\" }").Error.Should().Be(ImportErrors.SchemaUnknown);

        var duplicate = ImportAdapters.Parse("vibechat", """
            { "format": "vibechat.import.v1", "principals": [
              { "externalId": "U1", "displayName": "A" },
              { "externalId": "U1", "displayName": "B" }
            ] }
            """);
        ImportPolicies.Inspect(duplicate.Document!).Error.Should().Be(ImportErrors.DuplicateExternalId);

        var ambiguous = ImportAdapters.Parse("vibechat", """
            { "format": "vibechat.import.v1", "principals": [
              { "externalId": "U1", "displayName": "A", "mappedUserId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa" },
              { "externalId": "U2", "displayName": "B", "mappedUserId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa" }
            ] }
            """);
        ImportPolicies.Inspect(ambiguous.Document!).Error.Should().Be(ImportErrors.MappingAmbiguous);
    }

    [Fact]
    public void Hostile_attachment_is_quarantined_and_payload_is_dropped()
    {
        var parsed = ImportAdapters.Parse("vibechat", """
            {
              "format": "vibechat.import.v1",
              "principals": [{ "externalId": "U1", "displayName": "Ada" }],
              "channels": [{ "externalId": "C1", "name": "geral", "kind": "public" }],
              "messages": [{ "externalId": "M1", "channelExternalId": "C1", "authorExternalId": "U1", "body": "x", "createdAt": "2020-01-02T03:04:05Z" }],
              "attachments": [
                { "externalId": "A1", "messageExternalId": "M1", "fileName": "../evil.exe", "contentType": "application/x-msdownload", "byteLength": 4, "payloadBase64": "AAAA" },
                { "externalId": "A2", "messageExternalId": "M1", "fileName": "note.txt", "contentType": "text/plain", "byteLength": 50000000, "payloadBase64": "aGk=" }
              ]
            }
            """);
        var inspected = ImportPolicies.Inspect(parsed.Document!);
        inspected.Ok.Should().BeTrue();
        inspected.Report.Quarantined.Should().Be(2);
        inspected.Document!.Attachments.Should().OnlyContain(x => x.Quarantined && x.PayloadBase64 == null);
        JsonSerializer.Serialize(inspected.Report).Should().NotContain("AAAA");
    }

    [Fact]
    public void Zip_local_header_with_huge_uncompressed_size_is_rejected()
    {
        var header = new byte[30];
        header[0] = 0x50;
        header[1] = 0x4B;
        BitConverter.GetBytes(60_000_000u).CopyTo(header, 22);
        ImportPolicies.LooksLikeZipBomb(header, 20).Should().BeTrue();
    }

    [Fact]
    public void Quota_blocks_an_oversized_batch_and_accepts_two_hundred_messages()
    {
        var big = new CanonicalImport();
        for (var i = 0; i < 200; i++)
        {
            big.Messages.Add(new CanonicalMessage
            {
                ExternalId = $"M{i}",
                ChannelExternalId = "C1",
                AuthorExternalId = "U1",
                Body = "ok",
                CreatedAt = DateTimeOffset.Parse("2020-01-02T03:04:05Z").AddSeconds(i)
            });
        }

        big.Principals.Add(new CanonicalPrincipal { ExternalId = "U1", DisplayName = "Ada" });
        big.Channels.Add(new CanonicalChannel { ExternalId = "C1", Name = "geral" });
        ImportPolicies.Inspect(big).Ok.Should().BeTrue();

        big.Messages.Add(new CanonicalMessage
        {
            ExternalId = "overflow",
            ChannelExternalId = "C1",
            AuthorExternalId = "U1",
            Body = "x",
            CreatedAt = DateTimeOffset.UnixEpoch
        });
        while (big.Messages.Count <= ImportLimits.MaxMessages)
        {
            big.Messages.Add(new CanonicalMessage
            {
                ExternalId = $"X{big.Messages.Count}",
                ChannelExternalId = "C1",
                AuthorExternalId = "U1",
                Body = "x",
                CreatedAt = DateTimeOffset.UnixEpoch
            });
        }

        ImportPolicies.Inspect(big).Error.Should().Be(ImportErrors.Quota);
    }

    [Fact]
    public void Pause_and_resume_follow_the_state_machine()
    {
        ImportCommands.Allowed(ImportStatus.Planned, ImportCommands.Pause).Should().BeTrue();
        ImportCommands.Allowed(ImportStatus.Paused, ImportCommands.Resume).Should().BeTrue();
        ImportCommands.Allowed(ImportStatus.Paused, ImportCommands.Publish).Should().BeFalse();
        ImportCommands.Allowed(ImportStatus.Staged, ImportCommands.Execute).Should().BeFalse();
        ImportCommands.Allowed(ImportStatus.Published, ImportCommands.Rollback).Should().BeTrue();
    }

    [Fact]
    public void Import_permission_is_admin_only()
    {
        RolePermissionCatalog.For(Role.Admin).Should().Contain(Permissions.Workspace.Import);
        RolePermissionCatalog.For(Role.WorkspaceOwner).Should().Contain(Permissions.Workspace.Import);
        RolePermissionCatalog.For(Role.Member).Should().NotContain(Permissions.Workspace.Import);
        RolePermissionCatalog.For(Role.Auditor).Should().NotContain(Permissions.Workspace.Import);
        RolePermissionCatalog.For(Role.Moderator).Should().NotContain(Permissions.Workspace.Import);
        new ImportOptions().Enabled.Should().BeFalse();
    }

    [Fact]
    public void Secret_in_body_is_counted_and_absent_from_the_report()
    {
        var parsed = ImportAdapters.Parse("vibechat", """
            {
              "format": "vibechat.import.v1",
              "principals": [{ "externalId": "U1", "displayName": "Ada" }],
              "channels": [{ "externalId": "C1", "name": "geral", "kind": "public" }],
              "messages": [{
                "externalId": "M1", "channelExternalId": "C1", "authorExternalId": "U1",
                "body": "token sk-live-secret-value", "createdAt": "2020-01-02T03:04:05Z"
              }]
            }
            """);
        var inspected = ImportPolicies.Inspect(parsed.Document!);
        inspected.Report.Redacted.Should().Be(1);
        var report = JsonSerializer.Serialize(inspected.Report);
        report.Should().NotContain("sk-live");
        report.Should().NotContain("token");
    }
}
