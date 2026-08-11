namespace CustomerKyc.Api.DTOs;

public sealed class AuthRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
