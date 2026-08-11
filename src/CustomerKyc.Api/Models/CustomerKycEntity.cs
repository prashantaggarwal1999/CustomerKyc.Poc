namespace CustomerKyc.Api.Models;

public sealed class CustomerKycEntity
{
    public long Id { get; set; }
    public string CustomerReference { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string EncryptedPan { get; set; } = string.Empty;
    public string EncryptedAadhaar { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedOn { get; set; }
}
