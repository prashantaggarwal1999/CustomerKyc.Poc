using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace CustomerKyc.Api.OpenApi;

/// <summary>
/// Adds a JWT Bearer security scheme to the generated OpenAPI document
/// so Scalar UI shows the Authorize button.
///
/// Uses Microsoft.AspNetCore.OpenApi (built-in .NET 10) + Microsoft.OpenApi 2.x.
/// In OpenAPI 2.x the Microsoft.OpenApi.Models sub-namespace was removed;
/// all model types are directly under Microsoft.OpenApi.
/// </summary>
internal sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    private readonly IAuthenticationSchemeProvider _schemeProvider;

    public BearerSecuritySchemeTransformer(IAuthenticationSchemeProvider schemeProvider)
        => _schemeProvider = schemeProvider;

    public async Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        var schemes = await _schemeProvider.GetAllSchemesAsync();
        if (!schemes.Any(s => s.Name == JwtBearerDefaults.AuthenticationScheme))
            return;

        document.Components ??= new OpenApiComponents();
        // SecuritySchemes is IDictionary<string, IOpenApiSecurityScheme> in OpenAPI 2.x.
        // OpenApiSecurityScheme implements IOpenApiSecurityScheme.
        document.Components.SecuritySchemes ??=
            new Dictionary<string, IOpenApiSecurityScheme>();

        document.Components.SecuritySchemes[JwtBearerDefaults.AuthenticationScheme] =
            new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description =
                    "JWT Bearer token. Obtain one via POST /api/auth/token " +
                    "(username: poc-user, password: poc-password)."
            };
    }
}
