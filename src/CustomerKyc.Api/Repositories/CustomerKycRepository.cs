using CustomerKyc.Api.Data;
using CustomerKyc.Api.Models;
using Dapper;

namespace CustomerKyc.Api.Repositories;

public sealed class CustomerKycRepository : ICustomerKycRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public CustomerKycRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<long> InsertAsync(CustomerKycEntity entity)
    {
        const string sql = """
            INSERT INTO CustomerKyc
                (CustomerReference, FirstName, LastName, EncryptedPan, EncryptedAadhaar, Status, CreatedOn)
            VALUES
                (@CustomerReference, @FirstName, @LastName, @EncryptedPan, @EncryptedAadhaar, @Status, @CreatedOn);
            SELECT CAST(SCOPE_IDENTITY() AS BIGINT);
            """;

        using var connection = _connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<long>(sql, entity);
    }

    public async Task<CustomerKycEntity?> GetByIdAsync(long id)
    {
        const string sql = """
            SELECT Id, CustomerReference, FirstName, LastName,
                   EncryptedPan, EncryptedAadhaar, Status, CreatedOn
            FROM CustomerKyc
            WHERE Id = @Id;
            """;

        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<CustomerKycEntity>(sql, new { Id = id });
    }
}
