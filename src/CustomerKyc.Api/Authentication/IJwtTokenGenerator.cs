namespace CustomerKyc.Api.Authentication;

public interface IJwtTokenGenerator
{
    (string Token, DateTime ExpiresAt) GenerateToken(string username);
}
