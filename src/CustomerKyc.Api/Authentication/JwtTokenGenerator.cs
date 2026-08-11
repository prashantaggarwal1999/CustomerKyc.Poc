using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace CustomerKyc.Api.Authentication;

/// <summary>
/// Generates short-lived JWT Bearer tokens for the POC auth endpoint.
/// Uses System.IdentityModel.Tokens.Jwt — fully managed, Linux-compatible.
/// </summary>
public sealed class JwtTokenGenerator : IJwtTokenGenerator
{
    private readonly SymmetricSecurityKey _signingKey;
    private readonly string _issuer;
    private readonly string _audience;
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(1);

    public JwtTokenGenerator(string secret, string issuer, string audience)
    {
        ArgumentException.ThrowIfNullOrEmpty(secret);
        _signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        _issuer = issuer;
        _audience = audience;
    }

    public (string Token, DateTime ExpiresAt) GenerateToken(string username)
    {
        var expiresAt = DateTime.UtcNow.Add(TokenLifetime);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, username),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64)
        };

        var credentials = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAt,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
