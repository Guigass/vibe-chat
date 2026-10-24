using System.Security.Cryptography;

namespace VibeChat.TestHost;

/// <summary>Lab update key for B-141. Generated in memory. Not a production key.</summary>
public static class DesktopTestKeys
{
    public static RSA SigningKey { get; }
    public static string PublicKey { get; }

    static DesktopTestKeys()
    {
        SigningKey = RSA.Create(2048);
        PublicKey = Convert.ToBase64String(SigningKey.ExportSubjectPublicKeyInfo());
    }
}
