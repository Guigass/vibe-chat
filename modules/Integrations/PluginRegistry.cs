using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VibeChat.SharedKernel;

namespace VibeChat.Integrations;

/// <summary>
/// Signed plugin registry (B-137 / ADR-053). Packages are data. Nothing here
/// downloads or executes third-party code.
/// </summary>
public sealed class PluginRegistryOptions
{
    public const string SectionName = "Integrations:PluginRegistry";

    /// <summary>Off by default. Registry routes return 404 until an operator turns them on.</summary>
    public bool Enabled { get; set; }

    /// <summary>Any value is fail-closed. This slice does not fetch a remote catalog.</summary>
    public string? CatalogUrl { get; set; }

    public PluginRegistryTrustRootOptions[] TrustRoots { get; set; } = [];

    public string[] AllowlistedCatalogs { get; set; } = [];

    /// <summary>Operator kill list. Each item is <c>pluginId@version</c>.</summary>
    public string[] Revoked { get; set; } = [];
}

public sealed class PluginRegistryTrustRootOptions
{
    public string KeyId { get; set; } = string.Empty;

    /// <summary>SPKI PEM or base64 SubjectPublicKeyInfo. Never a private key.</summary>
    public string PublicKeyPem { get; set; } = string.Empty;

    public string[] Catalogs { get; set; } = [];
}

public static class PluginRegistryErrors
{
    public const string Disabled = "PluginRegistryDisabled";
    public const string InvalidIndex = "InvalidRegistryIndex";
    public const string InvalidSignature = "InvalidRegistrySignature";
    public const string InvalidChecksum = "InvalidRegistryChecksum";
    public const string UntrustedCatalog = "UntrustedCatalog";
    public const string VersionRevoked = "PluginVersionRevoked";
    public const string DowngradeRejected = "PluginDowngradeRejected";
    public const string CapabilityConfirmation = "PluginCapabilityConfirmationRequired";
    public const string GrantRequired = "PluginGrantRequired";
    public const string DependencyMissing = "PluginDependencyMissing";
    public const string EgressBlocked = "RegistryEgressBlocked";
    public const string IdempotencyConflict = "PluginRegistryIdempotencyConflict";
    public const string NotFound = "RegistryEntryNotFound";
    public const string CallbackRejected = "RegistryCallbackRejected";
}

public static class PluginRegistryRules
{
    public const string IndexSchema = "vibechat.registry-index.v1";
    public const string EntrySchema = "vibechat.registry-entry.v1";
    public const string SignaturePrefix = "rsapss-sha256=";
    public const string ChecksumPrefix = "sha256=";
    public const string ActiveStatus = "active";
    public const string RevokedStatus = "revoked";
    public const int MaxIndexBytes = 65_536;
    public const int MaxEntries = 32;
    public const int ContractVersion = 1;

    private static readonly string[] Sources = ["mirror", "official", "private", "allowlisted"];
    private static readonly string[] Risks = ["low", "medium", "high"];
    private static readonly string[] LocalSchemes = ["mirror", "official", "private"];
    private static readonly string[] PayloadProperties =
    [
        "advisory", "artifactRef", "capabilities", "catalogId", "checksum", "dependsOn", "keyId",
        "manifest", "maxContract", "minContract", "pluginId", "provenance", "publisher", "revoked",
        "risk", "schema", "source", "version"
    ];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static bool RemoteCatalogBlocked(string? catalogUrl) => !string.IsNullOrWhiteSpace(catalogUrl);

    public static int CompareVersion(string left, string right)
    {
        var a = left.Split('.').Select(int.Parse).ToArray();
        var b = right.Split('.').Select(int.Parse).ToArray();
        for (var i = 0; i < 3; i++)
        {
            var compared = a[i].CompareTo(b[i]);
            if (compared != 0)
            {
                return compared;
            }
        }

        return 0;
    }

    public static string[] AddedCapabilities(IReadOnlyCollection<string> current, IReadOnlyCollection<string> next) =>
        next.Where(item => !current.Contains(item, StringComparer.Ordinal)).Order(StringComparer.Ordinal).ToArray();

    public static bool Covers(IReadOnlyCollection<string> granted, IReadOnlyCollection<string> required) =>
        required.All(item => granted.Contains(item, StringComparer.Ordinal));

    public static string Checksum(string manifestCanonical)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(manifestCanonical));
        return ChecksumPrefix + Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string Fingerprint(string signature)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(signature));
        return Convert.ToHexString(hash).ToLowerInvariant()[..16];
    }

    public static string Sign(string privateKeyPem, string payload)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);
        var signature = rsa.SignData(Encoding.UTF8.GetBytes(payload), HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        return SignaturePrefix + Convert.ToBase64String(signature);
    }

    public static string CanonicalPayload(RegistryPayload payload)
    {
        var capabilities = string.Join(',', Sort(payload.Capabilities).Select(Quote));
        var dependsOn = string.Join(',', Sort(payload.DependsOn).Select(Quote));
        var advisory = payload.Advisory is null ? "null" : Quote(payload.Advisory);
        return "{"
            + "\"schema\":" + Quote(EntrySchema)
            + ",\"keyId\":" + Quote(payload.KeyId)
            + ",\"catalogId\":" + Quote(payload.CatalogId)
            + ",\"source\":" + Quote(payload.Source)
            + ",\"pluginId\":" + Quote(payload.PluginId)
            + ",\"version\":" + Quote(payload.Version)
            + ",\"publisher\":" + Quote(payload.Publisher)
            + ",\"artifactRef\":" + Quote(payload.ArtifactRef)
            + ",\"checksum\":" + Quote(payload.Checksum)
            + ",\"provenance\":" + Quote(payload.Provenance)
            + ",\"minContract\":" + Quote(payload.MinContract)
            + ",\"maxContract\":" + Quote(payload.MaxContract)
            + ",\"dependsOn\":[" + dependsOn + "]"
            + ",\"risk\":" + Quote(payload.Risk)
            + ",\"revoked\":" + (payload.Revoked ? "true" : "false")
            + ",\"advisory\":" + advisory
            + ",\"capabilities\":[" + capabilities + "]"
            + ",\"manifest\":" + Quote(payload.ManifestCanonical)
            + "}";
    }

    public static string WrapIndex(IEnumerable<(string Payload, string Signature)> entries)
    {
        var body = string.Join(',', entries.Select(entry =>
            "{\"payload\":" + Quote(entry.Payload) + ",\"signature\":" + Quote(entry.Signature) + "}"));
        return "{\"schema\":" + Quote(IndexSchema) + ",\"entries\":[" + body + "]}";
    }

    public static bool TryBuildIndex(string privateKeyPem, IReadOnlyList<RegistryPayload> payloads, out string index, out string error)
    {
        index = string.Empty;
        error = PluginRegistryErrors.InvalidIndex;
        if (payloads.Count is < 1 or > MaxEntries)
        {
            return false;
        }

        var signed = new List<(string Payload, string Signature)>(payloads.Count);
        foreach (var payload in payloads)
        {
            if (!TryPrepare(payload, out var canonical, out error))
            {
                return false;
            }

            signed.Add((canonical, Sign(privateKeyPem, canonical)));
        }

        index = WrapIndex(signed);
        error = string.Empty;
        return Encoding.UTF8.GetByteCount(index) <= MaxIndexBytes;
    }

    public static RegistryVerification Verify(string? indexJson, PluginRegistryTrust trust)
    {
        if (string.IsNullOrWhiteSpace(indexJson) || Encoding.UTF8.GetByteCount(indexJson) > MaxIndexBytes)
        {
            return RegistryVerification.Failed(PluginRegistryErrors.InvalidIndex);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(indexJson);
        }
        catch (JsonException)
        {
            return RegistryVerification.Failed(PluginRegistryErrors.InvalidIndex);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !Allowed(document.RootElement, ["schema", "entries"])
                || !ReadString(document.RootElement, "schema", out var schema)
                || schema != IndexSchema
                || !document.RootElement.TryGetProperty("entries", out var entries)
                || entries.ValueKind != JsonValueKind.Array
                || entries.GetArrayLength() is < 1 or > MaxEntries)
            {
                return RegistryVerification.Failed(PluginRegistryErrors.InvalidIndex);
            }

            var views = new List<RegistryEntryView>();
            foreach (var entry in entries.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object
                    || !Allowed(entry, ["payload", "signature"])
                    || !ReadString(entry, "payload", out var payload)
                    || !ReadString(entry, "signature", out var signature))
                {
                    return RegistryVerification.Failed(PluginRegistryErrors.InvalidIndex);
                }

                views.Add(VerifyEntry(payload, signature, trust));
            }

            if (!DependenciesResolve(views))
            {
                return RegistryVerification.Failed(PluginRegistryErrors.InvalidIndex);
            }

            return new RegistryVerification(true, null, views.ToArray());
        }
    }

    private static bool TryPrepare(RegistryPayload payload, out string canonical, out string error)
    {
        canonical = string.Empty;
        error = PluginRegistryErrors.InvalidIndex;
        if (!PluginManifestRules.TryParse(payload.ManifestCanonical, out var manifest, out error) || manifest is null)
        {
            return false;
        }

        if (manifest.Callback is not null)
        {
            error = PluginRegistryErrors.CallbackRejected;
            return false;
        }

        if (!string.Equals(manifest.CanonicalJson, payload.ManifestCanonical, StringComparison.Ordinal)
            || !string.Equals(manifest.Id, payload.PluginId, StringComparison.Ordinal)
            || !string.Equals(manifest.Version, payload.Version, StringComparison.Ordinal))
        {
            return false;
        }

        var checksum = Checksum(manifest.CanonicalJson);
        var prepared = payload with
        {
            Checksum = checksum,
            Capabilities = manifest.Capabilities,
            DependsOn = Sort(payload.DependsOn),
            ManifestCanonical = manifest.CanonicalJson
        };
        canonical = CanonicalPayload(prepared);
        error = string.Empty;
        return true;
    }

    private static RegistryEntryView VerifyEntry(string payload, string signature, PluginRegistryTrust trust)
    {
        var fingerprint = Fingerprint(signature);
        if (!TryReadPayload(payload, out var material, out var reason) || material is null)
        {
            return RegistryEntryView.Rejected(reason, fingerprint);
        }

        var canonical = CanonicalPayload(material);
        if (!string.Equals(canonical, payload, StringComparison.Ordinal))
        {
            return RegistryEntryView.Rejected(PluginRegistryErrors.InvalidSignature, fingerprint);
        }

        if (!string.Equals(material.Checksum, Checksum(material.ManifestCanonical), StringComparison.Ordinal))
        {
            return Rejected(material, signature, PluginRegistryErrors.InvalidChecksum);
        }

        if (!VerifySignature(material.KeyId, payload, signature, trust, material.CatalogId))
        {
            return Rejected(material, signature, PluginRegistryErrors.InvalidSignature);
        }

        if (!TrustedCatalog(material, trust))
        {
            return Rejected(material, signature, PluginRegistryErrors.UntrustedCatalog);
        }

        if (!LocalArtifact(material.ArtifactRef))
        {
            return Rejected(material, signature, PluginRegistryErrors.EgressBlocked);
        }

        if (material.Revoked || trust.Revoked.Contains(material.PluginId + "@" + material.Version, StringComparer.Ordinal))
        {
            return Rejected(material, signature, PluginRegistryErrors.VersionRevoked);
        }

        return Accepted(material, signature);
    }

    private static bool TryReadPayload(string payload, out RegistryPayload? material, out string error)
    {
        material = null;
        error = PluginRegistryErrors.InvalidIndex;
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payload);
        }
        catch (JsonException)
        {
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !Allowed(root, PayloadProperties))
            {
                return false;
            }

            if (!ReadString(root, "schema", out var schema) || schema != EntrySchema
                || !ReadString(root, "keyId", out var keyId) || keyId.Length is < 1 or > 64
                || !ReadString(root, "catalogId", out var catalogId) || catalogId.Length is < 1 or > 64
                || !ReadString(root, "source", out var source) || Array.IndexOf(Sources, source) < 0
                || !ReadString(root, "pluginId", out var pluginId)
                || !ReadString(root, "version", out var version)
                || !ReadString(root, "publisher", out var publisher) || publisher.Length is < 1 or > 80
                || !ReadString(root, "artifactRef", out var artifactRef)
                || !ReadString(root, "checksum", out var checksum) || !checksum.StartsWith(ChecksumPrefix, StringComparison.Ordinal)
                || !ReadString(root, "provenance", out var provenance) || provenance.Length is < 1 or > 120
                || !ReadString(root, "minContract", out var minContract) || !int.TryParse(minContract, out var min)
                || !ReadString(root, "maxContract", out var maxContract) || !int.TryParse(maxContract, out var max)
                || min > ContractVersion || max < ContractVersion
                || !ReadString(root, "risk", out var risk) || Array.IndexOf(Risks, risk) < 0
                || !root.TryGetProperty("revoked", out var revokedNode) || revokedNode.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                || !TryAdvisory(root, out var advisory)
                || !TryStringArray(root, "dependsOn", 8, 96, out var dependsOn)
                || !TryStringArray(root, "capabilities", 8, 64, out var capabilities)
                || !ReadString(root, "manifest", out var manifest))
            {
                return false;
            }

            if (!PluginManifestRules.TryParse(manifest, out var parsed, out error) || parsed is null)
            {
                return false;
            }

            if (parsed.Callback is not null)
            {
                error = PluginRegistryErrors.CallbackRejected;
                return false;
            }

            if (!string.Equals(parsed.CanonicalJson, manifest, StringComparison.Ordinal)
                || !string.Equals(parsed.Id, pluginId, StringComparison.Ordinal)
                || !string.Equals(parsed.Version, version, StringComparison.Ordinal)
                || !SameSet(parsed.Capabilities, capabilities))
            {
                return false;
            }

            foreach (var dependency in dependsOn)
            {
                var at = dependency.IndexOf('@');
                if (at <= 0 || dependency[(at + 1)..].Split('.').Length != 3)
                {
                    return false;
                }
            }

            material = new RegistryPayload(
                keyId, catalogId, source, pluginId, version, publisher, artifactRef, checksum, provenance,
                minContract, maxContract, dependsOn, risk, revokedNode.GetBoolean(), advisory, capabilities, manifest);
            error = string.Empty;
            return true;
        }
    }

    private static bool VerifySignature(string keyId, string payload, string signature, PluginRegistryTrust trust, string catalogId)
    {
        if (!signature.StartsWith(SignaturePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(signature[SignaturePrefix.Length..]);
        }
        catch (FormatException)
        {
            return false;
        }

        var data = Encoding.UTF8.GetBytes(payload);
        foreach (var root in trust.Roots)
        {
            if (!string.Equals(root.KeyId, keyId, StringComparison.Ordinal)
                || !root.Catalogs.Contains(catalogId, StringComparer.Ordinal))
            {
                continue;
            }

            try
            {
                using var rsa = LoadPublic(root.PublicKey);
                if (rsa.VerifyData(data, bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
                {
                    return true;
                }
            }
            catch (CryptographicException)
            {
                // A bad trust root does not become a valid signature.
            }
            catch (FormatException)
            {
            }
        }

        return false;
    }

    private static RSA LoadPublic(string material)
    {
        var rsa = RSA.Create();
        try
        {
            var trimmed = material.Trim();
            if (trimmed.Contains("BEGIN", StringComparison.Ordinal))
            {
                rsa.ImportFromPem(trimmed);
            }
            else
            {
                rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(trimmed), out _);
            }

            return rsa;
        }
        catch
        {
            rsa.Dispose();
            throw;
        }
    }

    private static bool TrustedCatalog(RegistryPayload payload, PluginRegistryTrust trust)
    {
        var root = trust.Roots.FirstOrDefault(item => string.Equals(item.KeyId, payload.KeyId, StringComparison.Ordinal));
        if (root is null || !root.Catalogs.Contains(payload.CatalogId, StringComparer.Ordinal))
        {
            return false;
        }

        if (payload.Source == "allowlisted")
        {
            return trust.AllowlistedCatalogs.Contains(payload.CatalogId, StringComparer.Ordinal);
        }

        return true;
    }

    private static bool LocalArtifact(string artifactRef)
    {
        if (artifactRef.Length is < 1 or > 512
            || artifactRef.Contains("..", StringComparison.Ordinal)
            || artifactRef.Contains('@', StringComparison.Ordinal)
            || !Uri.TryCreate(artifactRef, UriKind.Absolute, out var uri)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || string.IsNullOrEmpty(uri.Host))
        {
            return false;
        }

        return Array.IndexOf(LocalSchemes, uri.Scheme) >= 0;
    }

    private static bool DependenciesResolve(IReadOnlyList<RegistryEntryView> views)
    {
        var accepted = views.Where(item => item.Accepted).Select(item => item.PluginId + "@" + item.Version).ToHashSet(StringComparer.Ordinal);
        var graph = views.Where(item => item.Accepted).ToDictionary(item => item.PluginId + "@" + item.Version, item => item.DependsOn, StringComparer.Ordinal);
        foreach (var (id, dependencies) in graph)
        {
            if (dependencies.Contains(id, StringComparer.Ordinal) || dependencies.Any(item => !accepted.Contains(item)))
            {
                return false;
            }
        }

        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var id in graph.Keys)
        {
            if (VisitsCycle(id, graph, state))
            {
                return false;
            }
        }

        return true;
    }

    private static bool VisitsCycle(string id, IReadOnlyDictionary<string, string[]> graph, Dictionary<string, int> state)
    {
        if (state.TryGetValue(id, out var mark))
        {
            return mark == 1;
        }

        state[id] = 1;
        if (graph.TryGetValue(id, out var next))
        {
            foreach (var dependency in next)
            {
                if (VisitsCycle(dependency, graph, state))
                {
                    return true;
                }
            }
        }

        state[id] = 2;
        return false;
    }

    private static RegistryEntryView Accepted(RegistryPayload payload, string signature) => new(
        true, null, payload.KeyId, payload.CatalogId, payload.Source, payload.PluginId, payload.Version,
        payload.Publisher, payload.ArtifactRef, payload.Checksum, payload.Provenance, payload.Risk, payload.Revoked,
        payload.Advisory, Sort(payload.Capabilities), Sort(payload.DependsOn), payload.ManifestCanonical, CanonicalPayload(payload),
        signature, Fingerprint(signature));

    private static RegistryEntryView Rejected(RegistryPayload payload, string signature, string reason) => new(
        false, reason, payload.KeyId, payload.CatalogId, payload.Source, payload.PluginId, payload.Version,
        payload.Publisher, payload.ArtifactRef, payload.Checksum, payload.Provenance, payload.Risk, payload.Revoked || reason == PluginRegistryErrors.VersionRevoked,
        payload.Advisory, Sort(payload.Capabilities), Sort(payload.DependsOn), payload.ManifestCanonical, CanonicalPayload(payload),
        signature, Fingerprint(signature));

    private static bool TryAdvisory(JsonElement root, out string? advisory)
    {
        advisory = null;
        if (!root.TryGetProperty("advisory", out var node))
        {
            return false;
        }

        if (node.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (node.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        advisory = node.GetString();
        return advisory is { Length: <= 200 };
    }

    private static bool TryStringArray(JsonElement root, string name, int max, int maxItem, out string[] values)
    {
        values = [];
        if (!root.TryGetProperty(name, out var node) || node.ValueKind != JsonValueKind.Array || node.GetArrayLength() > max)
        {
            return false;
        }

        var list = new List<string>();
        foreach (var item in node.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var text = item.GetString() ?? string.Empty;
            if (text.Length < 1 || text.Length > maxItem || list.Contains(text, StringComparer.Ordinal))
            {
                return false;
            }

            list.Add(text);
        }

        values = list.ToArray();
        return true;
    }

    private static bool Allowed(JsonElement element, IReadOnlyCollection<string> names)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Contains(property.Name, StringComparer.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ReadString(JsonElement element, string name, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(name, out var node) || node.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = node.GetString() ?? string.Empty;
        return value.Length > 0;
    }

    private static bool SameSet(IReadOnlyCollection<string> left, IReadOnlyCollection<string> right) =>
        left.Count == right.Count && left.All(item => right.Contains(item, StringComparer.Ordinal));

    private static string[] Sort(IReadOnlyCollection<string> values) =>
        values.Order(StringComparer.Ordinal).ToArray();

    private static string Quote(string value) => JsonSerializer.Serialize(value, Json);
}

public sealed record RegistryPayload(
    string KeyId,
    string CatalogId,
    string Source,
    string PluginId,
    string Version,
    string Publisher,
    string ArtifactRef,
    string Checksum,
    string Provenance,
    string MinContract,
    string MaxContract,
    string[] DependsOn,
    string Risk,
    bool Revoked,
    string? Advisory,
    string[] Capabilities,
    string ManifestCanonical);

public sealed record RegistryTrustRoot(string KeyId, string PublicKey, string[] Catalogs);

public sealed record PluginRegistryTrust(RegistryTrustRoot[] Roots, string[] AllowlistedCatalogs, string[] Revoked);

public sealed record RegistryEntryView(
    bool Accepted,
    string? Reason,
    string KeyId,
    string CatalogId,
    string Source,
    string PluginId,
    string Version,
    string Publisher,
    string ArtifactRef,
    string Checksum,
    string Provenance,
    string Risk,
    bool Revoked,
    string? Advisory,
    string[] Capabilities,
    string[] DependsOn,
    string ManifestCanonical,
    string Payload,
    string Signature,
    string SignatureFingerprint)
{
    public static RegistryEntryView Rejected(string reason, string fingerprint) => new(
        false, reason, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
        string.Empty, string.Empty, string.Empty, string.Empty, false, null, [], [], string.Empty, string.Empty,
        string.Empty, fingerprint);
}

public sealed record RegistryVerification(bool Ok, string? Error, RegistryEntryView[] Entries)
{
    public static RegistryVerification Failed(string error) => new(false, error, []);
}

public sealed class PluginRegistryInstall
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public string PluginId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string? PreviousVersion { get; set; }
    public string Payload { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
    public string? PreviousPayload { get; set; }
    public string? PreviousSignature { get; set; }
    public string Publisher { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string CatalogId { get; set; } = string.Empty;
    public string KeyId { get; set; } = string.Empty;
    public string Checksum { get; set; } = string.Empty;
    public string ArtifactRef { get; set; } = string.Empty;
    public string Provenance { get; set; } = string.Empty;
    public string Risk { get; set; } = string.Empty;
    public string? Advisory { get; set; }
    public string[] Capabilities { get; set; } = [];
    public string[] GrantedCapabilities { get; set; } = [];
    public string[] DependsOn { get; set; } = [];
    public string Status { get; set; } = PluginRegistryRules.ActiveStatus;
    public Guid? InstalledPluginId { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? RequestHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class PluginRegistryQuarantine
{
    public Guid Id { get; set; }
    public TenantId TenantId { get; set; }
    public WorkspaceId WorkspaceId { get; set; }
    public string PluginId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public DateTimeOffset RevokedAt { get; set; }
}
