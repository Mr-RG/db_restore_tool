using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace db_restore_tool.Database
{
    public interface IConnectionProvider
    {
        Task EnsureConnectionAsync(CancellationToken cancellationToken = default);
        Task<string> GetConnectionStringAsync(string database = "master", CancellationToken cancellationToken = default);
        Task<SqlConnection> GetOpenConnectionAsync(string database = "master", CancellationToken cancellationToken = default);
    }

    public class ConnectionProvider : IConnectionProvider
    {
        private readonly RestoreConfig _config;
        private string _workingConnectionString;

        public ConnectionProvider(RestoreConfig config)
        {
            _config = config;
        }

        public async Task<string> GetConnectionStringAsync(string database = "master", CancellationToken cancellationToken = default)
        {
            if (_workingConnectionString != null)
                return new SqlConnectionStringBuilder(_workingConnectionString) { InitialCatalog = database }.ConnectionString;

            var authBuilder = new SqlConnectionStringBuilder
            {
                DataSource = _config.ServerName,
                UserID = _config.Username,
                Password = _config.Password,
                InitialCatalog = "master",
                TrustServerCertificate = true,
                Encrypt = false
            };

            if (await TryConnectAsync(authBuilder.ConnectionString, cancellationToken))
            {
                _workingConnectionString = authBuilder.ConnectionString;
                return new SqlConnectionStringBuilder(_workingConnectionString) { InitialCatalog = database }.ConnectionString;
            }
            
            var winBuilder = new SqlConnectionStringBuilder
            {
                DataSource = _config.ServerName,
                InitialCatalog = "master",
                IntegratedSecurity = true,
                TrustServerCertificate = true,
                Encrypt = false
            };

            if (await TryConnectAsync(winBuilder.ConnectionString, cancellationToken))
            {
                _workingConnectionString = winBuilder.ConnectionString;
                return new SqlConnectionStringBuilder(_workingConnectionString) { InitialCatalog = database }.ConnectionString;
            }

            throw new Exception("Could not connect to SQL Server. Both SQL Authentication and Windows Authentication failed.");
        }

        private async Task<bool> TryConnectAsync(string connectionString, CancellationToken cancellationToken)
        {
            try
            {
                await using var conn = new SqlConnection(connectionString);
                await conn.OpenAsync(cancellationToken);
                return true;
            }
            catch (SqlException)
            {
                return false;
            }
        }

        public async Task EnsureConnectionAsync(CancellationToken cancellationToken = default)
        {
            await GetConnectionStringAsync("master", cancellationToken);
        }

        public async Task<SqlConnection> GetOpenConnectionAsync(string database = "master", CancellationToken cancellationToken = default)
        {
            string connString = await GetConnectionStringAsync(database, cancellationToken);
            var conn = new SqlConnection(connString);
            await conn.OpenAsync(cancellationToken);
            return conn;
        }
    }
}
