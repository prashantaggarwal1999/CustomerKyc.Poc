using CustomerKyc.Api.Models;

namespace CustomerKyc.Api.Repositories;

public interface ICustomerKycRepository
{
    Task<long> InsertAsync(CustomerKycEntity entity);
    Task<CustomerKycEntity?> GetByIdAsync(long id);
}
