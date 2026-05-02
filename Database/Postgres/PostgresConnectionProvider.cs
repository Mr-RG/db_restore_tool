using System;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using db_restore_tool.Models;

namespace db_restore_tool.Database.Postgres
{
    public interface IPostgresConnectionProvider
    {
        Task<NpgsqlConnection> GetOpenConnectionAsync(string database = "postgres", CancellationToken cancellationToken = default);
        string GetConnectionString(string database = "postgres");
    }

    public class PostgresConnectionProvider : IPostgresConnectionProvider
    {
        private readonly ServerConfig _config;

        public PostgresConnectionProvider(ServerConfig config)
        {
            _config = config;
        }

        public string GetConnectionString(string database = "postgres")
        {
            return new NpgsqlConnectionStringBuilder
            {
                Host = _config.Host,
                Port = _config.Port > 0 ? _config.Port : 5432,
                Username = _config.Username,
                Password = _config.Password,
                Database = database,
                IncludeErrorDetail = true
            }.ConnectionString;
        }

        public async Task<NpgsqlConnection> GetOpenConnectionAsync(string database = "postgres", CancellationToken cancellationToken = default)
        {
            var conn = new NpgsqlConnection(GetConnectionString(database));
            await conn.OpenAsync(cancellationToken);
            return conn;
        }
    }
}
