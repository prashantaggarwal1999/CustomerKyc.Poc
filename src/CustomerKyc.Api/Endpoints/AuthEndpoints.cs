using CustomerKyc.Api.Authentication;
using CustomerKyc.Api.DTOs;

namespace CustomerKyc.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/token", (
            AuthRequest request,
            IJwtTokenGenerator tokenGenerator,
            IConfiguration configuration) =>
        {
            var expectedUsername = configuration["Auth:Username"] ?? "poc-user";
            var expectedPassword = configuration["Auth:Password"] ?? "poc-password";

            if (request.Username != expectedUsername || request.Password != expectedPassword)
                return Results.Unauthorized();

            var (token, expiresAt) = tokenGenerator.GenerateToken(request.Username);

            return Results.Ok(new AuthResponse
            {
                Token = token,
                TokenType = "Bearer",
                ExpiresAt = expiresAt
            });
        })
        .WithName("GetToken")
        .WithTags("Authentication")
        .WithSummary("POC: obtain a JWT Bearer token. Username: poc-user / Password: poc-password.")
        .AllowAnonymous();

        return app;
    }
}
