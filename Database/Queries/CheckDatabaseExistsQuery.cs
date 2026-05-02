using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace db_restore_tool.Database.Queries
{
    public class CheckDatabaseExistsQuery
    {
        private readonly IConnectionProvider _connectionProvider;

        public CheckDatabaseExistsQuery(IConnectionProvider connectionProvider)
        {
            _connectionProvider = connectionProvider;
        }

        public virtual async Task<bool> ExecuteAsync(string dbName, CancellationToken cancellationToken = default)
        {
            using var conn = await _connectionProvider.GetOpenConnectionAsync("master", cancellationToken);
            var sql = "SELECT COUNT(*) FROM sys.databases WHERE name = @dbName";
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@dbName", dbName);
            var result = await cmd.ExecuteScalarAsync(cancellationToken);
            return (int)result > 0;
        }
    }
}
