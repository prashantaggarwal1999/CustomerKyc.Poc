namespace CustomerKyc.Api.DTOs;

public sealed class CreateCustomerResponse
{
    public long Id { get; set; }
    public string CustomerReference { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}
