using System.Data;
using Microsoft.Data.SqlClient;

namespace CustomerKyc.Api.Data;

/// <summary>
/// Creates open SQL Server connections using Microsoft.Data.SqlClient.
/// Microsoft.Data.SqlClient uses a fully managed TDS protocol implementation
/// and works on Linux without any Windows-native dependency.
/// </summary>
public sealed class SqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public SqlConnectionFactory(string connectionString)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionString);
        _connectionString = connectionString;
    }

    public IDbConnection CreateConnection()
    {
        var connection = new SqlConnection(_connectionString);
        connection.Open();
        return connection;
    }
}
