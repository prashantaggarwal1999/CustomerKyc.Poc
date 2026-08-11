using CustomerKyc.Api.DTOs;
using CustomerKyc.Api.Validators;
using FluentAssertions;
using Xunit;

namespace CustomerKyc.Api.Tests.Validators;

public sealed class CustomerKycRequestValidatorTests
{
    private readonly CustomerKycRequestValidator _validator = new();

    private static CreateCustomerKycRequest ValidRequest() => new()
    {
        CustomerReference = "CUST-10001",
        FirstName = "John",
        LastName = "Doe",
        Pan = "ABCDE1234F",
        Aadhaar = "111122223333"
    };

    [Fact]
    public async Task Valid_request_passes_validation()
    {
        var result = await _validator.ValidateAsync(ValidRequest());
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_CustomerReference_fails(string value)
    {
        var req = ValidRequest();
        req.CustomerReference = value;
        var result = await _validator.ValidateAsync(req);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(req.CustomerReference));
    }

    [Fact]
    public async Task CustomerReference_exceeding_100_chars_fails()
    {
        var req = ValidRequest();
        req.CustomerReference = new string('A', 101);
        var result = await _validator.ValidateAsync(req);
        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("ABCD1234F")]   // 9 chars
    [InlineData("ABCDE12345")]  // digit in last position
    [InlineData("abcde1234F")]  // lowercase
    [InlineData("ABCDE1234FF")] // 11 chars
    public async Task Invalid_PAN_format_fails(string pan)
    {
        var req = ValidRequest();
        req.Pan = pan;
        var result = await _validator.ValidateAsync(req);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(req.Pan));
    }

    [Fact]
    public async Task Valid_PAN_passes()
    {
        var req = ValidRequest();
        req.Pan = "ABCDE1234F";
        var result = await _validator.ValidateAsync(req);
        result.Errors.Should().NotContain(e => e.PropertyName == nameof(req.Pan));
    }

    [Theory]
    [InlineData("11112222333")]   // 11 digits
    [InlineData("1111222233334")] // 13 digits
    [InlineData("ABCD22223333")]  // letters
    public async Task Invalid_Aadhaar_format_fails(string aadhaar)
    {
        var req = ValidRequest();
        req.Aadhaar = aadhaar;
        var result = await _validator.ValidateAsync(req);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(req.Aadhaar));
    }

    [Fact]
    public async Task Valid_Aadhaar_passes()
    {
        var req = ValidRequest();
        req.Aadhaar = "111122223333";
        var result = await _validator.ValidateAsync(req);
        result.Errors.Should().NotContain(e => e.PropertyName == nameof(req.Aadhaar));
    }

    [Theory]
    [InlineData(nameof(CreateCustomerKycRequest.FirstName))]
    [InlineData(nameof(CreateCustomerKycRequest.LastName))]
    public async Task Empty_name_fields_fail(string fieldName)
    {
        var req = ValidRequest();
        if (fieldName == nameof(req.FirstName)) req.FirstName = "";
        else req.LastName = "";

        var result = await _validator.ValidateAsync(req);
        result.IsValid.Should().BeFalse();
    }
}
