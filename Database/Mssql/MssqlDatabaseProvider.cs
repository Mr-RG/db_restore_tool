using System;
using System.Threading;
using System.Threading.Tasks;
using db_restore_tool.Models;
using db_restore_tool.Database.Mssql.Commands;
using db_restore_tool.Database.Mssql.Queries;

namespace db_restore_tool.Database.Mssql
{
    public class MssqlDatabaseProvider : IDatabaseProvider
    {
        private readonly IConnectionProvider _connectionProvider;
        private readonly CheckDatabaseExistsQuery _checkExistsQuery;
        private readonly GetDatabaseSizeQuery _getSizeQuery;
        private readonly GetBackupMetadataQuery _getMetadataQuery;
        private readonly KillConnectionsCommand _killConnectionsCommand;
        private readonly DropDatabaseCommand _dropDatabaseCommand;
        private readonly RestoreDatabaseCommand _restoreCommand;

        public string ProviderType => "MSSQL";

        public MssqlDatabaseProvider(ServerConfig serverConfig)
        {
            _connectionProvider = new MssqlConnectionProvider(serverConfig);
            _checkExistsQuery = new CheckDatabaseExistsQuery(_connectionProvider);
            _getSizeQuery = new GetDatabaseSizeQuery(_connectionProvider);
            _getMetadataQuery = new GetBackupMetadataQuery(_connectionProvider);
            _killConnectionsCommand = new KillConnectionsCommand(_connectionProvider);
            _dropDatabaseCommand = new DropDatabaseCommand(_connectionProvider);
            _restoreCommand = new RestoreDatabaseCommand(_connectionProvider, serverConfig);
        }

        public Task EnsureConnectionAsync(CancellationToken cancellationToken = default)
            => _connectionProvider.EnsureConnectionAsync(cancellationToken);

        public Task<bool> DatabaseExistsAsync(string dbName, CancellationToken cancellationToken = default)
            => _checkExistsQuery.ExecuteAsync(dbName, cancellationToken);

        public Task<double> GetDatabaseSizeMbAsync(string dbName, CancellationToken cancellationToken = default)
            => _getSizeQuery.ExecuteAsync(dbName, cancellationToken);

        public Task<BackupMetadata> GetBackupMetadataAsync(string backupFilePath, CancellationToken cancellationToken = default)
            => _getMetadataQuery.ExecuteAsync(backupFilePath, cancellationToken);

        public Task KillConnectionsAsync(string dbName, CancellationToken cancellationToken = default)
            => _killConnectionsCommand.ExecuteAsync(dbName, cancellationToken);

        public Task DropDatabaseAsync(string dbName, CancellationToken cancellationToken = default)
            => _dropDatabaseCommand.ExecuteAsync(dbName, cancellationToken);

        public Task RestoreDatabaseAsync(string dbName, string backupFilePath, BackupMetadata metadata, Action<string> messageCallback, CancellationToken cancellationToken = default)
            => _restoreCommand.ExecuteAsync(dbName, backupFilePath, metadata, messageCallback, cancellationToken);

        public async Task<System.Collections.Generic.List<string>> GetDatabasesAsync(CancellationToken cancellationToken = default)
        {
            var databases = new System.Collections.Generic.List<string>();
            await using var conn = await _connectionProvider.GetOpenConnectionAsync("master", cancellationToken);
            var sql = "SELECT name FROM sys.databases WHERE state_desc = 'ONLINE' ORDER BY name";
            await using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                databases.Add(reader.GetString(0));
            }
            return databases;
        }

        public async Task ExecuteQueryAsync(string dbName, string queryFilePath, string outputDir, Action<string> messageCallback, CancellationToken cancellationToken = default)
        {
            if (!System.IO.File.Exists(queryFilePath))
                throw new System.IO.FileNotFoundException($"Query file not found: {queryFilePath}");

            string query = await System.IO.File.ReadAllTextAsync(queryFilePath, cancellationToken);
            
            await using var conn = await _connectionProvider.GetOpenConnectionAsync(dbName, cancellationToken);
            conn.InfoMessage += (s, e) => messageCallback?.Invoke(e.Message);
            
            await using var cmd = new Microsoft.Data.SqlClient.SqlCommand(query, conn);
            cmd.CommandTimeout = 0; // Infinite timeout for long queries

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            await db_restore_tool.Services.QueryExecutorService.ProcessResultsAsync(reader, outputDir, messageCallback, cancellationToken);
        }
    }
}
