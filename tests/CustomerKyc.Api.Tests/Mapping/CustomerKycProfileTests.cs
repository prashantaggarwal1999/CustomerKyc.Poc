using AutoMapper;
using CustomerKyc.Api.DTOs;
using CustomerKyc.Api.Mapping;
using CustomerKyc.Api.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CustomerKyc.Api.Tests.Mapping;

public sealed class CustomerKycProfileTests
{
    private readonly IMapper _mapper;

    public CustomerKycProfileTests()
    {
        // AutoMapper 16.x requires ILoggerFactory as the second constructor argument.
        var config = new MapperConfiguration(
            cfg => cfg.AddProfile<CustomerKycProfile>(),
            NullLoggerFactory.Instance);
        config.AssertConfigurationIsValid();
        _mapper = config.CreateMapper();
    }

    [Fact]
    public void AutoMapper_configuration_is_valid()
    {
        // config.AssertConfigurationIsValid() in the constructor is the authoritative check.
        // This test documents that it passed.
        true.Should().BeTrue();
    }

    [Fact]
    public void Maps_CustomerKycEntity_to_CustomerKycResponse_correctly()
    {
        var entity = new CustomerKycEntity
        {
            Id = 42,
            CustomerReference = "CUST-10001",
            FirstName = "John",
            LastName = "Doe",
            EncryptedPan = "encrypted-pan-value",
            EncryptedAadhaar = "encrypted-aadhaar-value",
            Status = "Active",
            CreatedOn = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        var response = _mapper.Map<CustomerKycResponse>(entity);

        response.Id.Should().Be(42);
        response.CustomerReference.Should().Be("CUST-10001");
        response.FirstName.Should().Be("John");
        response.LastName.Should().Be("Doe");
        response.Status.Should().Be("Active");
        response.CreatedOn.Should().Be(entity.CreatedOn);

        // Pan/Aadhaar are intentionally ignored in AutoMapper; they are set
        // explicitly in the service after decryption.
        response.Pan.Should().BeNullOrEmpty();
        response.Aadhaar.Should().BeNullOrEmpty();
    }
}
