namespace CustomerKyc.Api.DTOs;

public sealed class CreateCustomerKycRequest
{
    public string CustomerReference { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Pan { get; set; } = string.Empty;
    public string Aadhaar { get; set; } = string.Empty;
}
