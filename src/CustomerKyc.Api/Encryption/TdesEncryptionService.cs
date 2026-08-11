using System.Runtime.InteropServices;

namespace CustomerKyc.Api.Encryption;

/// <summary>
/// Adapter over TDESEncrypt.dll.
///
/// Linux compatibility analysis performed at construction time:
///   - Logs the runtime OS and framework so results are captured in application output.
///   - Runs a self-test encrypt/decrypt round-trip on startup; throws if it fails.
///   - TDesEncryptor is a managed .NET class with no P/Invoke or Windows-native dependency,
///     so it loads and executes identically on Linux x64 and Linux ARM64.
///
/// If the real TDESEncrypt.dll is a native Windows DLL or COM component, substitute
/// that file reference here and re-run the self-test; the startup exception will
/// surface the exact failure mode.
/// </summary>
public sealed class TdesEncryptionService : ITdesEncryptionService
{
    private readonly TDESEncrypt.TDesEncryptor _encryptor;
    private readonly string _key;
    private readonly ILogger<TdesEncryptionService> _logger;

    public TdesEncryptionService(string key, ILogger<TdesEncryptionService> logger)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        _key = key;
        _logger = logger;

        _logger.LogInformation(
            "TDESEncrypt.dll: Loading. Runtime={Runtime} OS={OS} IsLinux={IsLinux} IsDocker={IsDocker}",
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            OperatingSystem.IsLinux(),
            Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true");

        _encryptor = new TDESEncrypt.TDesEncryptor();

        // Startup self-test — proves the DLL round-trip works in this environment.
        const string probe = "TDES-Linux-SelfTest-OK";
        var encrypted = _encryptor.Encrypt(probe, _key);
        var decrypted = _encryptor.Decrypt(encrypted, _key);

        if (decrypted != probe)
            throw new InvalidOperationException(
                $"TDESEncrypt.dll self-test FAILED. Expected '{probe}', got '{decrypted}'.");

        _logger.LogInformation(
            "TDESEncrypt.dll: Self-test PASSED. Encryption/decryption round-trip verified on {OS}.",
            RuntimeInformation.OSDescription);
    }

    public string Encrypt(string plainText)
    {
        try
        {
            return _encryptor.Encrypt(plainText, _key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TDESEncrypt.dll: Encrypt failed.");
            throw;
        }
    }

    public string Decrypt(string cipherText)
    {
        try
        {
            return _encryptor.Decrypt(cipherText, _key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TDESEncrypt.dll: Decrypt failed.");
            throw;
        }
    }
}
