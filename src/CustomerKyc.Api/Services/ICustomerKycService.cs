using CustomerKyc.Api.DTOs;

namespace CustomerKyc.Api.Services;

public interface ICustomerKycService
{
    Task<CreateCustomerResponse> CreateAsync(CreateCustomerKycRequest request);
    Task<CustomerKycResponse?> GetByIdAsync(long id);
}
