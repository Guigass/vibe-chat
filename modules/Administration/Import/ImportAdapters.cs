using System.Globalization;
using System.Text.Json;

namespace VibeChat.Administration;

public static class ImportAdapters
{
    public static readonly string[] Supported = ["vibechat", "slack", "mattermost", "discord"];

    public static ImportParseResult Parse(string? adapter, string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Fail(ImportErrors.DocumentInvalid);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return Fail(ImportErrors.DocumentInvalid);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Fail(ImportErrors.DocumentInvalid);
            }

            var name = (adapter ?? string.Empty).Trim().ToLowerInvariant();
            return name switch
            {
                "vibechat" or "canonical" => ParseVibeChat(document.RootElement),
                "slack" => ParseSlack(document.RootElement),
                "mattermost" => ParseMattermost(document.RootElement),
                "discord" => ParseDiscord(document.RootElement),
                _ => Fail(ImportErrors.SchemaUnknown)
            };
        }
    }

    private static ImportParseResult ParseVibeChat(JsonElement root)
    {
        var format = ReadString(root, "format");
        if (!string.Equals(format, ImportLimits.Format, StringComparison.Ordinal))
        {
            return Fail(ImportErrors.SchemaUnknown);
        }

        var import = new CanonicalImport
        {
            Format = ImportLimits.Format,
            SourceSystem = ReadString(root, "source", "system") ?? "vibechat",
            AdapterVersion = ReadString(root, "source", "adapterVersion") ?? "1",
            TenantClaimIgnored = root.TryGetProperty("tenantId", out _) || root.TryGetProperty("tenant_id", out _)
        };

        foreach (var item in Array(root, "principals"))
        {
            var externalId = Required(item, "externalId");
            if (externalId is null)
            {
                return Fail(ImportErrors.DocumentInvalid);
            }

            import.Principals.Add(new CanonicalPrincipal
            {
                ExternalId = externalId,
                DisplayName = ReadString(item, "displayName") ?? externalId,
                Role = ReadString(item, "role"),
                MappedUserId = ReadGuid(item, "mappedUserId"),
                Historical = !item.TryGetProperty("mappedUserId", out var mapped) || mapped.ValueKind is JsonValueKind.Null
            });
        }

        foreach (var item in Array(root, "spaces"))
        {
            var externalId = Required(item, "externalId");
            var name = Required(item, "name");
            if (externalId is null || name is null)
            {
                return Fail(ImportErrors.DocumentInvalid);
            }

            import.Spaces.Add(new CanonicalSpace { ExternalId = externalId, Name = name });
        }

        foreach (var item in Array(root, "channels"))
        {
            var externalId = Required(item, "externalId");
            var name = Required(item, "name");
            if (externalId is null || name is null)
            {
                return Fail(ImportErrors.DocumentInvalid);
            }

            import.Channels.Add(new CanonicalChannel
            {
                ExternalId = externalId,
                SpaceExternalId = ReadString(item, "spaceExternalId"),
                Name = name,
                Kind = (ReadString(item, "kind") ?? "public").ToLowerInvariant()
            });
        }

        foreach (var item in Array(root, "threads"))
        {
            var externalId = Required(item, "externalId");
            var channel = Required(item, "channelExternalId");
            var rootId = Required(item, "rootMessageExternalId");
            if (externalId is null || channel is null || rootId is null)
            {
                return Fail(ImportErrors.DocumentInvalid);
            }

            import.Threads.Add(new CanonicalThread
            {
                ExternalId = externalId,
                ChannelExternalId = channel,
                RootMessageExternalId = rootId
            });
        }

        foreach (var item in Array(root, "messages"))
        {
            var externalId = Required(item, "externalId");
            var channel = Required(item, "channelExternalId");
            var author = Required(item, "authorExternalId");
            if (externalId is null || channel is null || author is null || !TryReadTime(item, "createdAt", out var createdAt))
            {
                return Fail(ImportErrors.DocumentInvalid);
            }

            import.Messages.Add(new CanonicalMessage
            {
                ExternalId = externalId,
                ChannelExternalId = channel,
                ThreadExternalId = ReadString(item, "threadExternalId"),
                AuthorExternalId = author,
                Body = ReadString(item, "body") ?? string.Empty,
                CreatedAt = createdAt
            });
        }

        foreach (var item in Array(root, "attachments"))
        {
            var externalId = Required(item, "externalId");
            var message = Required(item, "messageExternalId");
            if (externalId is null || message is null)
            {
                return Fail(ImportErrors.DocumentInvalid);
            }

            import.Attachments.Add(new CanonicalAttachment
            {
                ExternalId = externalId,
                MessageExternalId = message,
                FileName = ReadString(item, "fileName") ?? "file",
                ContentType = ReadString(item, "contentType") ?? "application/octet-stream",
                ByteLength = ReadLong(item, "byteLength"),
                Sha256 = ReadString(item, "sha256"),
                PayloadBase64 = ReadString(item, "payloadBase64")
            });
        }

        return new ImportParseResult(true, null, import);
    }

    private static ImportParseResult ParseSlack(JsonElement root)
    {
        var import = new CanonicalImport { SourceSystem = "slack", TenantClaimIgnored = false };
        var spaceId = "slack-default";
        import.Spaces.Add(new CanonicalSpace { ExternalId = spaceId, Name = "Importados" });

        foreach (var user in Array(root, "users"))
        {
            if (user.TryGetProperty("deleted", out var deleted) && deleted.ValueKind == JsonValueKind.True)
            {
                continue;
            }

            var id = Required(user, "id");
            if (id is null)
            {
                return Fail(ImportErrors.DocumentInvalid);
            }

            var profile = user.TryGetProperty("profile", out var profileNode) ? profileNode : default;
            var name = ReadString(profile, "display_name")
                ?? ReadString(profile, "real_name")
                ?? ReadString(user, "real_name")
                ?? ReadString(user, "name")
                ?? id;
            var owner = Truth(user, "is_owner") || Truth(user, "is_primary_owner");
            import.Principals.Add(new CanonicalPrincipal
            {
                ExternalId = id,
                DisplayName = name,
                Role = owner ? "WorkspaceOwner" : Truth(user, "is_admin") ? "Admin" : "Member"
            });
        }

        foreach (var channel in Array(root, "channels"))
        {
            var id = Required(channel, "id") ?? ReadString(channel, "name");
            var name = Required(channel, "name");
            if (id is null || name is null)
            {
                return Fail(ImportErrors.DocumentInvalid);
            }

            import.Channels.Add(new CanonicalChannel
            {
                ExternalId = id,
                SpaceExternalId = spaceId,
                Name = name,
                Kind = Truth(channel, "is_private") ? "private" : "public"
            });

            foreach (var message in Array(channel, "messages"))
            {
                var ts = ReadString(message, "ts");
                var user = ReadString(message, "user") ?? ReadString(message, "bot_id");
                if (ts is null || user is null || !TrySlackTime(ts, out var createdAt))
                {
                    continue;
                }

                var threadTs = ReadString(message, "thread_ts");
                var threadId = string.IsNullOrEmpty(threadTs) || threadTs == ts ? null : threadTs;
                if (threadId is not null && import.Threads.All(x => x.ExternalId != threadId))
                {
                    import.Threads.Add(new CanonicalThread
                    {
                        ExternalId = threadId,
                        ChannelExternalId = id,
                        RootMessageExternalId = threadId
                    });
                }

                import.Messages.Add(new CanonicalMessage
                {
                    ExternalId = ts,
                    ChannelExternalId = id,
                    ThreadExternalId = threadId,
                    AuthorExternalId = user,
                    Body = ReadString(message, "text") ?? string.Empty,
                    CreatedAt = createdAt
                });
            }
        }

        return new ImportParseResult(true, null, import);
    }

    private static ImportParseResult ParseMattermost(JsonElement root)
    {
        var import = new CanonicalImport { SourceSystem = "mattermost" };
        var spaces = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var user in Array(root, "users"))
        {
            var id = Required(user, "id");
            if (id is null)
            {
                return Fail(ImportErrors.DocumentInvalid);
            }

            var roles = ReadString(user, "roles") ?? "system_user";
            var forbidden = roles.Contains("owner", StringComparison.OrdinalIgnoreCase);
            var admin = roles.Contains("system_admin", StringComparison.OrdinalIgnoreCase);
            import.Principals.Add(new CanonicalPrincipal
            {
                ExternalId = id,
                DisplayName = ReadString(user, "username") ?? id,
                Role = forbidden ? "WorkspaceOwner" : admin ? "Admin" : "Member"
            });
        }

        foreach (var channel in Array(root, "channels"))
        {
            var id = Required(channel, "id");
            var name = Required(channel, "name");
            if (id is null || name is null)
            {
                return Fail(ImportErrors.DocumentInvalid);
            }

            var team = ReadString(channel, "team_name") ?? "imported";
            if (!spaces.ContainsKey(team))
            {
                spaces[team] = team;
                import.Spaces.Add(new CanonicalSpace { ExternalId = team, Name = team });
            }

            var privateKind = string.Equals(ReadString(channel, "type"), "P", StringComparison.OrdinalIgnoreCase);
            import.Channels.Add(new CanonicalChannel
            {
                ExternalId = id,
                SpaceExternalId = team,
                Name = name,
                Kind = privateKind ? "private" : "public"
            });

            foreach (var post in Array(channel, "posts"))
            {
                var postId = Required(post, "id");
                var user = Required(post, "user_id");
                if (postId is null || user is null)
                {
                    return Fail(ImportErrors.DocumentInvalid);
                }

                var createdAt = DateTimeOffset.FromUnixTimeMilliseconds(ReadLong(post, "create_at"));
                var rootId = ReadString(post, "root_id");
                string? threadId = null;
                if (!string.IsNullOrEmpty(rootId))
                {
                    threadId = rootId;
                    if (import.Threads.All(x => x.ExternalId != rootId))
                    {
                        import.Threads.Add(new CanonicalThread
                        {
                            ExternalId = rootId,
                            ChannelExternalId = id,
                            RootMessageExternalId = rootId
                        });
                    }
                }

                import.Messages.Add(new CanonicalMessage
                {
                    ExternalId = postId,
                    ChannelExternalId = id,
                    ThreadExternalId = threadId,
                    AuthorExternalId = user,
                    Body = ReadString(post, "message") ?? string.Empty,
                    CreatedAt = createdAt
                });
            }
        }

        if (import.Spaces.Count == 0)
        {
            import.Spaces.Add(new CanonicalSpace { ExternalId = "imported", Name = "Importados" });
        }

        return new ImportParseResult(true, null, import);
    }

    private static ImportParseResult ParseDiscord(JsonElement root)
    {
        var import = new CanonicalImport { SourceSystem = "discord" };
        var guild = root.TryGetProperty("guild", out var guildNode) ? ReadString(guildNode, "name") : null;
        var spaceName = string.IsNullOrWhiteSpace(guild) ? "Importados" : guild;
        import.Spaces.Add(new CanonicalSpace { ExternalId = "discord-guild", Name = spaceName });
        import.TenantClaimIgnored = root.TryGetProperty("tenantId", out _);

        foreach (var channel in Array(root, "channels"))
        {
            var id = Required(channel, "id");
            var name = Required(channel, "name");
            if (id is null || name is null)
            {
                return Fail(ImportErrors.DocumentInvalid);
            }

            import.Channels.Add(new CanonicalChannel
            {
                ExternalId = id,
                SpaceExternalId = "discord-guild",
                Name = name,
                Kind = "public"
            });

            foreach (var message in Array(channel, "messages"))
            {
                var messageId = Required(message, "id");
                if (messageId is null || !message.TryGetProperty("author", out var author))
                {
                    return Fail(ImportErrors.DocumentInvalid);
                }

                var authorId = Required(author, "id");
                if (authorId is null || !TryReadTime(message, "timestamp", out var createdAt))
                {
                    return Fail(ImportErrors.DocumentInvalid);
                }

                if (import.Principals.All(x => x.ExternalId != authorId))
                {
                    import.Principals.Add(new CanonicalPrincipal
                    {
                        ExternalId = authorId,
                        DisplayName = ReadString(author, "username") ?? authorId,
                        Role = "Member"
                    });
                }

                string? threadId = null;
                if (message.TryGetProperty("message_reference", out var reference))
                {
                    threadId = ReadString(reference, "message_id");
                    if (!string.IsNullOrEmpty(threadId) && import.Threads.All(x => x.ExternalId != threadId))
                    {
                        import.Threads.Add(new CanonicalThread
                        {
                            ExternalId = threadId,
                            ChannelExternalId = id,
                            RootMessageExternalId = threadId
                        });
                    }
                }

                import.Messages.Add(new CanonicalMessage
                {
                    ExternalId = messageId,
                    ChannelExternalId = id,
                    ThreadExternalId = threadId,
                    AuthorExternalId = authorId,
                    Body = ReadString(message, "content") ?? string.Empty,
                    CreatedAt = createdAt
                });
            }
        }

        return new ImportParseResult(true, null, import);
    }

    private static ImportParseResult Fail(string error) => new(false, error, null);

    private static IEnumerable<JsonElement> Array(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in value.EnumerateArray())
        {
            yield return item;
        }
    }

    private static string? Required(JsonElement item, string name)
    {
        var value = ReadString(item, name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? ReadString(JsonElement item, string name)
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static string? ReadString(JsonElement item, string objectName, string name)
    {
        if (!item.TryGetProperty(objectName, out var child) || child.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return ReadString(child, name);
    }

    private static Guid? ReadGuid(JsonElement item, string name)
    {
        var value = ReadString(item, name);
        return Guid.TryParse(value, out var id) && id != Guid.Empty ? id : null;
    }

    private static long ReadLong(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value))
        {
            return 0;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out var parsed) ? parsed : 0;
    }

    private static bool Truth(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static bool TryReadTime(JsonElement item, string name, out DateTimeOffset createdAt)
    {
        createdAt = default;
        var value = ReadString(item, name);
        return value is not null && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out createdAt);
    }

    private static bool TrySlackTime(string ts, out DateTimeOffset createdAt)
    {
        createdAt = default;
        if (!decimal.TryParse(ts, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            return false;
        }

        var ms = (long)Math.Round(seconds * 1000m, 0, MidpointRounding.AwayFromZero);
        createdAt = DateTimeOffset.FromUnixTimeMilliseconds(ms);
        return true;
    }
}
