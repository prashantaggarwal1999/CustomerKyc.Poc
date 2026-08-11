namespace CustomerKyc.Api.DTOs;

public sealed class EncryptionTestResponse
{
    public bool Success { get; set; }
    public string Original { get; set; } = string.Empty;
    public string Encrypted { get; set; } = string.Empty;
    public string Decrypted { get; set; } = string.Empty;
    public string? Error { get; set; }
    public string Runtime { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
}
