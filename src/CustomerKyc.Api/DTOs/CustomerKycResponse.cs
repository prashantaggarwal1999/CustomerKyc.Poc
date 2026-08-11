namespace CustomerKyc.Api.DTOs;

/// <remarks>
/// POC-ONLY: PAN and Aadhaar are returned decrypted here solely to verify
/// that the TDES round-trip works end-to-end. Never expose decrypted PII
/// in a production endpoint.
/// </remarks>
public sealed class CustomerKycResponse
{
    public long Id { get; set; }
    public string CustomerReference { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Pan { get; set; } = string.Empty;
    public string Aadhaar { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedOn { get; set; }
}
