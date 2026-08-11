using System.Runtime.InteropServices;
using CustomerKyc.Api.DTOs;
using CustomerKyc.Api.Encryption;

namespace CustomerKyc.Api.Endpoints;

public static class EncryptionTestEndpoints
{
    public static IEndpointRouteBuilder MapEncryptionTestEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/encryption/test", (
            EncryptionTestRequest request,
            ITdesEncryptionService encryption,
            ILogger<Program> logger) =>
        {
            if (string.IsNullOrEmpty(request.Value))
                return Results.BadRequest(new { error = "Value is required." });

            try
            {
                var encrypted = encryption.Encrypt(request.Value);
                var decrypted = encryption.Decrypt(encrypted);
                var success = decrypted == request.Value;

                logger.LogInformation(
                    "Encryption test: original={Original} encrypted={Encrypted} decrypted={Decrypted} success={Success}",
                    request.Value, encrypted, decrypted, success);

                return Results.Ok(new EncryptionTestResponse
                {
                    Success = success,
                    Original = request.Value,
                    Encrypted = encrypted,
                    Decrypted = decrypted,
                    Runtime = RuntimeInformation.FrameworkDescription,
                    Platform = RuntimeInformation.OSDescription
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Encryption test FAILED for value: {Value}", request.Value);

                return Results.Ok(new EncryptionTestResponse
                {
                    Success = false,
                    Original = request.Value,
                    Encrypted = string.Empty,
                    Decrypted = string.Empty,
                    Error = ex.Message,
                    Runtime = RuntimeInformation.FrameworkDescription,
                    Platform = RuntimeInformation.OSDescription
                });
            }
        })
        .WithName("EncryptionTest")
        .WithTags("Encryption")
        .WithSummary("Encrypt → Decrypt round-trip test using TDESEncrypt.dll. Primary Linux compatibility proof.")
        .RequireAuthorization();

        return app;
    }
}
