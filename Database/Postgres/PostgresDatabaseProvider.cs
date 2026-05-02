using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using db_restore_tool.Models;

namespace db_restore_tool.Database.Postgres
{
    public class PostgresDatabaseProvider : IDatabaseProvider
    {
        private readonly IPostgresConnectionProvider _connectionProvider;
        private readonly ServerConfig _config;

        public string ProviderType => "PostgreSQL";

        public PostgresDatabaseProvider(ServerConfig config)
        {
            _config = config;
            _connectionProvider = new PostgresConnectionProvider(config);
        }

        public async Task EnsureConnectionAsync(CancellationToken cancellationToken = default)
        {
            await using var conn = await _connectionProvider.GetOpenConnectionAsync("postgres", cancellationToken);
        }

        public async Task<bool> DatabaseExistsAsync(string dbName, CancellationToken cancellationToken = default)
        {
            await using var conn = await _connectionProvider.GetOpenConnectionAsync("postgres", cancellationToken);
            await using var cmd = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @dbName", conn);
            cmd.Parameters.AddWithValue("dbName", dbName);
            var result = await cmd.ExecuteScalarAsync(cancellationToken);
            return result != null;
        }

        public async Task<double> GetDatabaseSizeMbAsync(string dbName, CancellationToken cancellationToken = default)
        {
            await using var conn = await _connectionProvider.GetOpenConnectionAsync("postgres", cancellationToken);
            await using var cmd = new NpgsqlCommand("SELECT pg_database_size(@dbName)", conn);
            cmd.Parameters.AddWithValue("dbName", dbName);
            var result = await cmd.ExecuteScalarAsync(cancellationToken);
            if (result != null && result != DBNull.Value)
            {
                long sizeBytes = Convert.ToInt64(result);
                return sizeBytes / (1024.0 * 1024.0);
            }
            return 0;
        }

        public Task<BackupMetadata> GetBackupMetadataAsync(string backupFilePath, CancellationToken cancellationToken = default)
        {
            // PostgreSQL dumps don't typically have easily readable headers without running pg_restore -l
            // We'll return a basic metadata
            long size = new FileInfo(backupFilePath).Length;
            return Task.FromResult(new BackupMetadata
            {
                RequiredSpaceBytes = size, // Roughly estimate space needed is same as backup size
                DatabaseName = ""
            });
        }

        public async Task KillConnectionsAsync(string dbName, CancellationToken cancellationToken = default)
        {
            await using var conn = await _connectionProvider.GetOpenConnectionAsync("postgres", cancellationToken);
            await using var cmd = new NpgsqlCommand(@"
                SELECT pg_terminate_backend(pid) 
                FROM pg_stat_activity 
                WHERE datname = @dbName AND pid <> pg_backend_pid()", conn);
            cmd.Parameters.AddWithValue("dbName", dbName);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task DropDatabaseAsync(string dbName, CancellationToken cancellationToken = default)
        {
            await using var conn = await _connectionProvider.GetOpenConnectionAsync("postgres", cancellationToken);
            await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{dbName}\"", conn);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task RestoreDatabaseAsync(string dbName, string backupFilePath, BackupMetadata metadata, Action<string> messageCallback, CancellationToken cancellationToken = default)
        {
            // First, create the database
            await using (var conn = await _connectionProvider.GetOpenConnectionAsync("postgres", cancellationToken))
            {
                await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{dbName}\"", conn);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            // Execute pg_restore or psql
            string tool = backupFilePath.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) ? "psql" : "pg_restore";
            string args = tool == "psql"
                ? $"-h {_config.Host} -p {(_config.Port > 0 ? _config.Port : 5432)} -U {_config.Username} -d \"{dbName}\" -f \"{backupFilePath}\""
                : $"-h {_config.Host} -p {(_config.Port > 0 ? _config.Port : 5432)} -U {_config.Username} -d \"{dbName}\" -v \"{backupFilePath}\"";

            var psi = new ProcessStartInfo
            {
                FileName = tool,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            // Set password in environment variable to avoid prompting
            psi.EnvironmentVariables["PGPASSWORD"] = _config.Password;

            using var process = Process.Start(psi);
            if (process == null) throw new Exception($"Failed to start {tool}. Is it in your PATH?");

            process.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) messageCallback(e.Data); };
            process.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) messageCallback(e.Data); };

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                throw new Exception($"{tool} exited with code {process.ExitCode}. See log for details.");
            }
        }

        public async Task<System.Collections.Generic.List<string>> GetDatabasesAsync(CancellationToken cancellationToken = default)
        {
            var databases = new System.Collections.Generic.List<string>();
            await using var conn = await _connectionProvider.GetOpenConnectionAsync("postgres", cancellationToken);
            var sql = "SELECT datname FROM pg_database WHERE datistemplate = false ORDER BY datname";
            await using var cmd = new NpgsqlCommand(sql, conn);
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
            conn.Notice += (s, e) => messageCallback?.Invoke(e.Notice.MessageText);
            
            await using var cmd = new NpgsqlCommand(query, conn);
            cmd.CommandTimeout = 0;

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            await db_restore_tool.Services.QueryExecutorService.ProcessResultsAsync(reader, outputDir, messageCallback, cancellationToken);
        }
    }
}
