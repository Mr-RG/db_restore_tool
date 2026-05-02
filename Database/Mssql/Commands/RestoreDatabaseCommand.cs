using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using db_restore_tool.Models;

namespace db_restore_tool.Database.Mssql.Commands
{
    public class RestoreDatabaseCommand
    {
        private readonly IConnectionProvider _connectionProvider;
        private readonly ServerConfig _config;

        public RestoreDatabaseCommand(IConnectionProvider connectionProvider, ServerConfig config)
        {
            _connectionProvider = connectionProvider;
            _config = config;
        }

        public virtual async Task ExecuteAsync(string dbName, string backupFilePath, BackupMetadata metadata, Action<string> progressCallback = null, CancellationToken cancellationToken = default)
        {
            if (metadata == null || !metadata.IsValid)
                throw new ArgumentException("Invalid backup metadata provided.");

            string DataLocationPath = Path.Combine(_config.DataLocation);
            Directory.CreateDirectory(DataLocationPath);
            
            string dataFileName = Path.Combine(_config.DataLocation, $"{dbName}.mdf");
            string logFileName = Path.Combine(_config.DataLocation, $"{dbName}_log.ldf");

            progressCallback?.Invoke($"Target Data File: {dataFileName}");
            progressCallback?.Invoke($"Target Log File : {logFileName}");

            var restoreSql = $@"RESTORE DATABASE [{dbName}] FROM DISK = N'{backupFilePath}' WITH MOVE N'{metadata.LogicalDataName}' TO N'{dataFileName}', MOVE N'{metadata.LogicalLogName}' TO N'{logFileName}', NOUNLOAD, REPLACE, STATS = 5";

            using var conn = await _connectionProvider.GetOpenConnectionAsync("master", cancellationToken);
            conn.FireInfoMessageEventOnUserErrors = true;
            conn.InfoMessage += (sender, e) => { progressCallback?.Invoke(e.Message); };
            
            using var cmd = new SqlCommand(restoreSql, conn);
            cmd.CommandTimeout = 0; // Infinite timeout for long restores
            
            try
            {
                // Use Task.Run with synchronous ExecuteNonQuery because ExecuteNonQueryAsync 
                // buffers InfoMessage events until the very end, preventing real-time progress.
                using (cancellationToken.Register(() => cmd.Cancel()))
                {
                    await Task.Run(() => cmd.ExecuteNonQuery(), cancellationToken);
                }
            }
            catch (SqlException ex)
            {
                if (ex.Number == 3)
                {
                    throw new Exception($"SQL Server cannot find the specified path: '{_config.DataLocation}'. Ensure the folder exists on the SQL SERVER machine and that the SQL Server service account (e.g., NT Service\\MSSQLSERVER) has Full Control permissions on it. Original Error: {ex.Message}", ex);
                }
                if (ex.Message.Contains("operating system error 32") || ex.Message.Contains("in use"))
                {
                    throw new Exception($"Failed to restore. A database file is in use. Recommendation: Run KillConnections on the target database or manually stop the service locking '{dataFileName}' or '{logFileName}'. Original Error: {ex.Message}", ex);
                }
                throw;
            }
        }
    }
}
