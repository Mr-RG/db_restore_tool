using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace db_restore_tool.Database.Mssql.Queries
{
    public class GetDatabaseSizeQuery
    {
        private readonly IConnectionProvider _connectionProvider;

        public GetDatabaseSizeQuery(IConnectionProvider connectionProvider)
        {
            _connectionProvider = connectionProvider;
        }

        public virtual async Task<double> ExecuteAsync(string dbName, CancellationToken cancellationToken = default)
        {
            using var conn = await _connectionProvider.GetOpenConnectionAsync("master", cancellationToken);
            using var cmd = new SqlCommand("SELECT SUM(size) * 8.0 / 1024.0 FROM sys.master_files WHERE database_id = DB_ID(@dbName)", conn);
            cmd.Parameters.AddWithValue("@dbName", dbName);
            var result = await cmd.ExecuteScalarAsync(cancellationToken);
            return (result != null && result != DBNull.Value) ? Convert.ToDouble(result) : 0;
        }
    }
}
