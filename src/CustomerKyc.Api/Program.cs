using System.Runtime.InteropServices;
using System.Text;
using AspNetCoreRateLimit;
using CustomerKyc.Api.Authentication;
using CustomerKyc.Api.Data;
using CustomerKyc.Api.Encryption;
using CustomerKyc.Api.Endpoints;
using CustomerKyc.Api.Mapping;
using CustomerKyc.Api.OpenApi;
using CustomerKyc.Api.Repositories;
using CustomerKyc.Api.Services;
using CustomerKyc.Api.Validators;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// ── Logging ────────────────────────────────────────────────────────────────
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

using var bootstrapLoggerFactory = LoggerFactory.Create(lb => lb.AddConsole());
var startupLogger = bootstrapLoggerFactory.CreateLogger("Startup");

startupLogger.LogInformation("═══════════════════════════════════════════════════");
startupLogger.LogInformation(" Customer KYC API — Linux/Docker POC");
startupLogger.LogInformation("═══════════════════════════════════════════════════");
startupLogger.LogInformation(" Runtime : {Runtime}", RuntimeInformation.FrameworkDescription);
startupLogger.LogInformation(" OS      : {OS}", RuntimeInformation.OSDescription);
startupLogger.LogInformation(" Arch    : {Arch}", RuntimeInformation.OSArchitecture);
startupLogger.LogInformation(" Linux   : {IsLinux}", OperatingSystem.IsLinux());
startupLogger.LogInformation(" Docker  : {IsDocker}",
    Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true");
startupLogger.LogInformation("═══════════════════════════════════════════════════");

// ── System.Configuration.ConfigurationManager — Linux compatibility check ──
// Package: System.Configuration.ConfigurationManager 10.0.8
// Verdict: FULLY COMPATIBLE on Linux — fully managed, reads from *.dll.config.
// In practice, prefer IConfiguration for all greenfield ASP.NET Core config.
try
{
    var appSettings = System.Configuration.ConfigurationManager.AppSettings;
    startupLogger.LogInformation(
        "System.Configuration.ConfigurationManager: LOADED OK. Keys={Count}. PocCheck={Val}",
        appSettings.Count,
        appSettings["PocCompatibilityCheck"] ?? "(not set)");
}
catch (Exception ex)
{
    startupLogger.LogWarning(ex, "System.Configuration.ConfigurationManager: {Msg}", ex.Message);
}

var configuration = builder.Configuration;

// ── Memory Cache (required by AspNetCoreRateLimit) ─────────────────────────
builder.Services.AddMemoryCache();

// ── AspNetCoreRateLimit 5.0.0 ──────────────────────────────────────────────
// Compatible with Minimal APIs via IpRateLimitMiddleware.
// No Windows dependency — uses IMemoryCache only.
builder.Services.Configure<IpRateLimitOptions>(configuration.GetSection("IpRateLimiting"));
builder.Services.Configure<IpRateLimitPolicies>(configuration.GetSection("IpRateLimitPolicies"));
builder.Services.AddSingleton<IIpPolicyStore, MemoryCacheIpPolicyStore>();
builder.Services.AddSingleton<IRateLimitCounterStore, MemoryCacheRateLimitCounterStore>();
builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();
builder.Services.AddSingleton<IProcessingStrategy, AsyncKeyLockProcessingStrategy>();
builder.Services.AddInMemoryRateLimiting();

// ── JWT Authentication ──────────────────────────────────────────────────────
var jwtSecret = configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Jwt:Secret is required. Set via env var Jwt__Secret.");
var jwtIssuer = configuration["Jwt:Issuer"]
    ?? throw new InvalidOperationException("Jwt:Issuer is required.");
var jwtAudience = configuration["Jwt:Audience"]
    ?? throw new InvalidOperationException("Jwt:Audience is required.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

// ── OpenAPI — built-in .NET 10 + Scalar UI ─────────────────────────────────
// Replaces Swashbuckle.AspNetCore.
// Microsoft.AspNetCore.OpenApi ships with the .NET 10 shared framework.
// Scalar.AspNetCore provides the browser UI at /scalar/v1.
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = "Customer KYC API";
        document.Info.Version = "v1";
        document.Info.Description =
            "Customer KYC Linux/Docker Compatibility POC — .NET 10 Minimal APIs. " +
            "Primary goal: validate TDESEncrypt.dll on Linux.";
        return Task.CompletedTask;
    });
});

// ── AutoMapper 16.1.1 ───────────────────────────────────────────────────────
// In AutoMapper 16.x the AddAutoMapper(Type) overload was removed.
// Use the Action<IMapperConfigurationExpression> overload instead.
builder.Services.AddAutoMapper(cfg => cfg.AddProfile<CustomerKycProfile>());

// ── FluentValidation 12.1.1 ─────────────────────────────────────────────────
builder.Services.AddValidatorsFromAssemblyContaining<CustomerKycRequestValidator>();

// ── Database (Microsoft.Data.SqlClient 7.0.1 + Dapper 2.1.79) ──────────────
builder.Services.AddSingleton<IDbConnectionFactory>(_ =>
{
    var cs = configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");
    return new SqlConnectionFactory(cs);
});

// ── TDESEncrypt.dll Encryption Service ─────────────────────────────────────
// The service constructor runs a startup self-test; any load failure throws here.
builder.Services.AddSingleton<ITdesEncryptionService>(sp =>
{
    var key = configuration["Encryption:Key"]
        ?? throw new InvalidOperationException("Encryption:Key is required. Set via env var Encryption__Key.");
    var logger = sp.GetRequiredService<ILogger<TdesEncryptionService>>();
    return new TdesEncryptionService(key, logger);
});

// ── Application Services ────────────────────────────────────────────────────
builder.Services.AddScoped<ICustomerKycRepository, CustomerKycRepository>();
builder.Services.AddScoped<ICustomerKycService, CustomerKycService>();

// ── JWT Token Generator ─────────────────────────────────────────────────────
builder.Services.AddSingleton<IJwtTokenGenerator>(_ =>
    new JwtTokenGenerator(jwtSecret, jwtIssuer, jwtAudience));

// ══════════════════════════════════════════════════════════════════════════════

var app = builder.Build();

// ── Middleware Pipeline ─────────────────────────────────────────────────────
app.UseIpRateLimiting();

// Built-in OpenAPI document endpoint: GET /openapi/v1.json
app.MapOpenApi();

// Scalar browser UI: http://localhost:{port}/scalar/v1
app.MapScalarApiReference(opts =>
{
    opts.Title = "Customer KYC API";
    opts.AddPreferredSecuritySchemes("Bearer");
});

app.UseAuthentication();
app.UseAuthorization();

// ── Endpoints ───────────────────────────────────────────────────────────────
app.MapHealthEndpoints();
app.MapAuthEndpoints();
app.MapCustomerEndpoints();
app.MapEncryptionTestEndpoints();

app.Run();

// Exposes Program to WebApplicationFactory in integration tests.
public partial class Program { }
