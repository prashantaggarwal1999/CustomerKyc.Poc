using CustomerKyc.Api.DTOs;
using CustomerKyc.Api.Services;
using CustomerKyc.Api.Validators;
using FluentValidation;

namespace CustomerKyc.Api.Endpoints;

public static class CustomerEndpoints
{
    public static IEndpointRouteBuilder MapCustomerEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/customers", async (
            CreateCustomerKycRequest request,
            IValidator<CreateCustomerKycRequest> validator,
            ICustomerKycService service) =>
        {
            var validation = await validator.ValidateAsync(request);
            if (!validation.IsValid)
                return Results.ValidationProblem(validation.ToDictionary());

            var result = await service.CreateAsync(request);
            return Results.Created($"/api/customers/{result.Id}", result);
        })
        .WithName("CreateCustomer")
        .WithTags("Customers")
        .WithSummary("Create a customer KYC record. PAN and Aadhaar are encrypted via TDESEncrypt.dll.")
        .RequireAuthorization();

        // POC-ONLY: returns decrypted PAN/Aadhaar to validate round-trip.
        app.MapGet("/api/customers/{id:long}", async (
            long id,
            ICustomerKycService service) =>
        {
            var result = await service.GetByIdAsync(id);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("GetCustomer")
        .WithTags("Customers")
        .WithSummary("POC-ONLY: Returns decrypted PAN/Aadhaar to verify TDES round-trip.")
        .RequireAuthorization();

        return app;
    }
}
