using CustomerKyc.Api.DTOs;
using FluentValidation;

namespace CustomerKyc.Api.Validators;

public sealed class CustomerKycRequestValidator : AbstractValidator<CreateCustomerKycRequest>
{
    // PAN format: 5 uppercase letters, 4 digits, 1 uppercase letter (ABCDE1234F)
    private const string PanPattern = @"^[A-Z]{5}[0-9]{4}[A-Z]{1}$";

    // Aadhaar: exactly 12 digits
    private const string AadhaarPattern = @"^\d{12}$";

    public CustomerKycRequestValidator()
    {
        RuleFor(x => x.CustomerReference)
            .NotEmpty().WithMessage("Customer reference is required.")
            .MaximumLength(100).WithMessage("Customer reference must not exceed 100 characters.");

        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("First name is required.")
            .MaximumLength(100).WithMessage("First name must not exceed 100 characters.");

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("Last name is required.")
            .MaximumLength(100).WithMessage("Last name must not exceed 100 characters.");

        RuleFor(x => x.Pan)
            .NotEmpty().WithMessage("PAN is required.")
            .Length(10).WithMessage("PAN must be exactly 10 characters.")
            .Matches(PanPattern).WithMessage("PAN format is invalid. Expected format: ABCDE1234F.");

        RuleFor(x => x.Aadhaar)
            .NotEmpty().WithMessage("Aadhaar is required.")
            .Length(12).WithMessage("Aadhaar must be exactly 12 digits.")
            .Matches(AadhaarPattern).WithMessage("Aadhaar must contain only digits.");
    }
}
