using System.Runtime.InteropServices;
using CustomerKyc.Api.Encryption;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace CustomerKyc.Api.Tests.Encryption;

/// <summary>
/// These tests are the most critical part of the Linux compatibility POC.
/// They prove that TDESEncrypt.dll (TDesEncryptor) encrypts and decrypts
/// correctly in the current runtime environment.
///
/// Expected result on Linux: all tests PASS.
/// If TDESEncrypt.dll is a native Windows binary, tests will throw at
/// service construction and the failure message will identify the root cause.
/// </summary>
public sealed class TdesEncryptionServiceTests
{
    private const string TestKey = "test-encryption-key-that-is-long-enough!!";
    private readonly ITestOutputHelper _output;

    public TdesEncryptionServiceTests(ITestOutputHelper output) => _output = output;

    private TdesEncryptionService CreateService() =>
        new(TestKey, NullLogger<TdesEncryptionService>.Instance);

    [Fact]
    public void TDESEncrypt_dll_loads_on_current_platform()
    {
        _output.WriteLine($"Runtime : {RuntimeInformation.FrameworkDescription}");
        _output.WriteLine($"OS      : {RuntimeInformation.OSDescription}");
        _output.WriteLine($"IsLinux : {OperatingSystem.IsLinux()}");

        // Construction runs the startup self-test; any load failure throws here.
        var service = CreateService();
        service.Should().NotBeNull();
    }

    [Theory]
    [InlineData("Linux-TDES-Test-123")]
    [InlineData("Hello, World!")]
    [InlineData("ABCDE1234F")]           // PAN-like value
    [InlineData("111122223333")]          // Aadhaar-like value
    [InlineData("a")]
    [InlineData("The quick brown fox jumps over the lazy dog.")]
    public void Encrypt_then_Decrypt_returns_original_value(string plainText)
    {
        var service = CreateService();

        var encrypted = service.Encrypt(plainText);
        var decrypted = service.Decrypt(encrypted);

        _output.WriteLine($"Plain    : {plainText}");
        _output.WriteLine($"Encrypted: {encrypted}");
        _output.WriteLine($"Decrypted: {decrypted}");

        encrypted.Should().NotBe(plainText, "encrypted value must differ from plain text");
        decrypted.Should().Be(plainText, "decrypted value must equal original plain text");
    }

    [Fact]
    public void Same_input_produces_same_ciphertext_ECB_mode()
    {
        var service = CreateService();
        var enc1 = service.Encrypt("deterministic");
        var enc2 = service.Encrypt("deterministic");

        // ECB is deterministic — same key + same plaintext = same ciphertext
        enc1.Should().Be(enc2);
    }

    [Fact]
    public void Different_plaintexts_produce_different_ciphertexts()
    {
        var service = CreateService();
        var enc1 = service.Encrypt("value-one");
        var enc2 = service.Encrypt("value-two");
        enc1.Should().NotBe(enc2);
    }

    [Fact]
    public void Encrypt_returns_base64_string()
    {
        var service = CreateService();
        var encrypted = service.Encrypt("test");
        var bytes = Convert.FromBase64String(encrypted); // throws if not valid base64
        bytes.Should().NotBeEmpty();
    }
}
