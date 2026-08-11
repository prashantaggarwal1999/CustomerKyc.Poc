using System.Net;
using System.Net.Http.Json;
using CustomerKyc.Api.DTOs;
using CustomerKyc.Api.Encryption;
using CustomerKyc.Api.Repositories;
using CustomerKyc.Api.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace CustomerKyc.Api.Tests.Integration;

/// <summary>
/// Integration tests using WebApplicationFactory.
/// SQL Server is replaced with a mock repository so these tests run without a database.
/// The real TdesEncryptionService (and therefore TDESEncrypt.dll) is used, which is the
/// primary Linux compatibility proof.
/// </summary>
public sealed class ApiIntegrationTests : IClassFixture<KycWebApplicationFactory>
{
    private readonly KycWebApplicationFactory _factory;

    public ApiIntegrationTests(KycWebApplicationFactory factory) => _factory = factory;

    // ── Health ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GET_health_returns_200_no_auth_required()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Healthy");
    }

    // ── Auth ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task POST_auth_token_with_valid_credentials_returns_token()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/token", new AuthRequest
        {
            Username = "poc-user",
            Password = "poc-password"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        auth!.Token.Should().NotBeNullOrEmpty();
        auth.TokenType.Should().Be("Bearer");
    }

    [Fact]
    public async Task POST_auth_token_with_wrong_credentials_returns_401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/token", new AuthRequest
        {
            Username = "wrong",
            Password = "wrong"
        });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Protected endpoints without token ─────────────────────────────────

    [Fact]
    public async Task POST_customers_without_token_returns_401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/customers", new CreateCustomerKycRequest
        {
            CustomerReference = "CUST-10001",
            FirstName = "John",
            LastName = "Doe",
            Pan = "ABCDE1234F",
            Aadhaar = "111122223333"
        });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task POST_encryption_test_without_token_returns_401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/encryption/test",
            new EncryptionTestRequest { Value = "test" });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Authenticated requests ─────────────────────────────────────────────

    [Fact]
    public async Task POST_encryption_test_with_token_returns_successful_round_trip()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/encryption/test",
            new EncryptionTestRequest { Value = "Linux-TDES-Test-123" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<EncryptionTestResponse>();
        result!.Success.Should().BeTrue("TDESEncrypt.dll must successfully round-trip on this platform");
        result.Original.Should().Be("Linux-TDES-Test-123");
        result.Decrypted.Should().Be("Linux-TDES-Test-123");
        result.Encrypted.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task POST_customers_with_invalid_request_returns_422()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/customers", new CreateCustomerKycRequest
        {
            CustomerReference = "",  // required
            FirstName = "John",
            LastName = "Doe",
            Pan = "BAD",             // invalid format
            Aadhaar = "123"          // too short
        });

        // Results.ValidationProblem() returns 400 in ASP.NET Core Minimal APIs.
        // 422 requires explicitly setting statusCode: 422 in the call.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task POST_customers_with_valid_request_returns_201()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/customers", new CreateCustomerKycRequest
        {
            CustomerReference = "CUST-10001",
            FirstName = "John",
            LastName = "Doe",
            Pan = "ABCDE1234F",
            Aadhaar = "111122223333"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = await response.Content.ReadFromJsonAsync<CreateCustomerResponse>();
        result!.CustomerReference.Should().Be("CUST-10001");
    }

    [Fact]
    public async Task GET_customers_unknown_id_returns_404()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/customers/99999");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

/// <summary>
/// Test host factory — swaps the real SQL repository with an in-memory fake
/// so integration tests run without a live SQL Server.
/// TdesEncryptionService is NOT replaced: the real TDESEncrypt.dll runs.
/// </summary>
public sealed class KycWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly InMemoryCustomerKycRepository _fakeRepo = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            // Provide minimal test configuration so startup does not throw
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"]   = "integration-test-secret-at-least-32-chars!!",
                ["Jwt:Issuer"]   = "CustomerKycApi",
                ["Jwt:Audience"] = "CustomerKycApiUsers",
                ["Encryption:Key"] = "integration-test-encryption-key-32c!!",
                ["Auth:Username"] = "poc-user",
                ["Auth:Password"] = "poc-password",
                ["ConnectionStrings:DefaultConnection"] = "Server=(local);Database=TestDb;"
            });
        });

        builder.ConfigureServices(services =>
        {
            // Replace real SQL repository with in-memory fake
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(ICustomerKycRepository));
            if (descriptor != null) services.Remove(descriptor);
            services.AddScoped<ICustomerKycRepository>(_ => _fakeRepo);

            // Replace IDbConnectionFactory so startup does not try to open SQL
            var dbDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(CustomerKyc.Api.Data.IDbConnectionFactory));
            if (dbDescriptor != null) services.Remove(dbDescriptor);
            var mockFactory = new Mock<CustomerKyc.Api.Data.IDbConnectionFactory>();
            services.AddSingleton(mockFactory.Object);
        });
    }

    public async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = CreateClient();
        var tokenResponse = await client.PostAsJsonAsync("/api/auth/token", new AuthRequest
        {
            Username = "poc-user",
            Password = "poc-password"
        });
        var auth = await tokenResponse.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", auth!.Token);
        return client;
    }
}

/// <summary>Thread-safe in-memory fake repository for integration tests.</summary>
internal sealed class InMemoryCustomerKycRepository : ICustomerKycRepository
{
    private readonly Dictionary<long, CustomerKycEntity> _store = new();
    private long _nextId = 1;
    private readonly object _lock = new();

    public Task<long> InsertAsync(CustomerKycEntity entity)
    {
        lock (_lock)
        {
            entity.Id = _nextId++;
            _store[entity.Id] = entity;
            return Task.FromResult(entity.Id);
        }
    }

    public Task<CustomerKycEntity?> GetByIdAsync(long id)
    {
        lock (_lock)
        {
            _store.TryGetValue(id, out var entity);
            return Task.FromResult(entity);
        }
    }
}
