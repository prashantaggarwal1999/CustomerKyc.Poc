using System.Runtime.InteropServices;

namespace CustomerKyc.Api.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", () => Results.Ok(new
        {
            status = "Healthy",
            runtime = RuntimeInformation.FrameworkDescription,
            os = RuntimeInformation.OSDescription,
            isLinux = OperatingSystem.IsLinux(),
            isDocker = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true",
            utcNow = DateTime.UtcNow
        }))
        .WithName("Health")
        .WithTags("Health")
        .WithSummary("Health check — no authentication required.")
        .AllowAnonymous();

        return app;
    }
}
