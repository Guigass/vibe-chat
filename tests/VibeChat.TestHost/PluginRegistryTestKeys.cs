using System.Security.Cryptography;

namespace VibeChat.TestHost;

/// <summary>Lab trust root for B-137. Generated in memory. Not a production key.</summary>
public static class PluginRegistryTestKeys
{
    public static string PrivatePem { get; }
    public static string PublicKey { get; }

    static PluginRegistryTestKeys()
    {
        using var rsa = RSA.Create(2048);
        PrivatePem = rsa.ExportPkcs8PrivateKeyPem();
        PublicKey = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());
    }
}
