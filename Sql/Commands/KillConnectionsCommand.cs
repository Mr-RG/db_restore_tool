using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace mssql_db_restore.Sql.Commands
{
    public class KillConnectionsCommand
    {
        private readonly IConnectionProvider _connectionProvider;

        public KillConnectionsCommand(IConnectionProvider connectionProvider)
        {
            _connectionProvider = connectionProvider;
        }

        public virtual async Task ExecuteAsync(string dbName, CancellationToken cancellationToken = default)
        {
            using var conn = await _connectionProvider.GetOpenConnectionAsync("master", cancellationToken);
            var sql = $@"IF EXISTS (SELECT name FROM sys.databases WHERE name = N'{dbName}') BEGIN ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; END";
            using var cmd = new SqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
