namespace CustomerKyc.Api.Encryption;

public interface ITdesEncryptionService
{
    string Encrypt(string plainText);
    string Decrypt(string cipherText);
}
