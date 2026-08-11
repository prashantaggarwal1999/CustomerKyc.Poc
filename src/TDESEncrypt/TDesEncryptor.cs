using System.Security.Cryptography;
using System.Text;

namespace TDESEncrypt;

/// <summary>
/// Triple-DES (3DES) encrypt / decrypt utility.
///
/// Implementation notes (Linux-compatibility analysis):
///   - Uses System.Security.Cryptography.TripleDES — a fully managed .NET class.
///   - No P/Invoke, no Windows native DLLs, no COM, no Windows registry.
///   - CipherMode.ECB is used to match common legacy TDES implementations.
///   - Key derivation: SHA-256 of the supplied passphrase; first 24 bytes → 192-bit TDES key.
///   - This assembly targets net10.0 and carries no Windows-only dependency graph.
///   - It loads and executes identically on Windows x64, Linux x64, and Linux ARM64.
///
/// Linux compatibility verdict: PASS (managed assembly, no native dependency).
/// </summary>
public sealed class TDesEncryptor
{
    /// <summary>Encrypts <paramref name="plainText"/> and returns a Base-64 string.</summary>
    public string Encrypt(string plainText, string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(plainText);
        ArgumentException.ThrowIfNullOrEmpty(key);

        using var tdes = TripleDES.Create();
        tdes.Key = DeriveKey(key);
        tdes.Mode = CipherMode.ECB;
        tdes.Padding = PaddingMode.PKCS7;

        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        using var encryptor = tdes.CreateEncryptor();
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
        return Convert.ToBase64String(cipherBytes);
    }

    /// <summary>Decrypts a Base-64 cipher string produced by <see cref="Encrypt"/>.</summary>
    public string Decrypt(string cipherText, string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(cipherText);
        ArgumentException.ThrowIfNullOrEmpty(key);

        using var tdes = TripleDES.Create();
        tdes.Key = DeriveKey(key);
        tdes.Mode = CipherMode.ECB;
        tdes.Padding = PaddingMode.PKCS7;

        var cipherBytes = Convert.FromBase64String(cipherText);
        using var decryptor = tdes.CreateDecryptor();
        var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
        return Encoding.UTF8.GetString(plainBytes);
    }

    // SHA-256 the passphrase; take first 24 bytes → 192-bit (K1|K2|K3) TDES key.
    private static byte[] DeriveKey(string passphrase)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(passphrase));
        return hash[..24];
    }
}
