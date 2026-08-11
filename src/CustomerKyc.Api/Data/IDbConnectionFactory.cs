using System.Data;

namespace CustomerKyc.Api.Data;

public interface IDbConnectionFactory
{
    IDbConnection CreateConnection();
}
