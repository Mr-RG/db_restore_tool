using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace mssql_db_restore.Sql.Queries
{
    public class BackupMetadata
    {
        public string DatabaseName { get; set; }
        public long RequiredSpaceBytes { get; set; }
        public string LogicalDataName { get; set; }
        public string LogicalLogName { get; set; }
        public bool IsValid => !string.IsNullOrEmpty(LogicalDataName) && !string.IsNullOrEmpty(LogicalLogName);
    }

    public class GetBackupMetadataQuery
    {
        private readonly IConnectionProvider _connectionProvider;

        public GetBackupMetadataQuery(IConnectionProvider connectionProvider)
        {
            _connectionProvider = connectionProvider;
        }

        public virtual async Task<BackupMetadata> ExecuteAsync(string backupFilePath, CancellationToken cancellationToken = default)
        {
            var metadata = new BackupMetadata();
            
            using var conn = await _connectionProvider.GetOpenConnectionAsync("master", cancellationToken);
            
            // 1. Get Header Info
            using (var cmd = new SqlCommand($"RESTORE HEADERONLY FROM DISK = N'{backupFilePath}'", conn))
            using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
            {
                if (await reader.ReadAsync(cancellationToken))
                {
                    metadata.DatabaseName = reader["DatabaseName"]?.ToString();
                }
            }

            // 2. Get File List Info
            using (var cmd = new SqlCommand($"RESTORE FILELISTONLY FROM DISK = N'{backupFilePath}'", conn))
            using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
            {
                long totalSpaceBytes = 0;
                while (await reader.ReadAsync(cancellationToken))
                {
                    var type = reader["Type"]?.ToString();
                    var logicalName = reader["LogicalName"]?.ToString();
                    
                    if (type == "D" && string.IsNullOrEmpty(metadata.LogicalDataName)) 
                        metadata.LogicalDataName = logicalName;
                    else if (type == "L" && string.IsNullOrEmpty(metadata.LogicalLogName)) 
                        metadata.LogicalLogName = logicalName;

                    if (long.TryParse(reader["Size"]?.ToString(), out long sizeBytes))
                    {
                        totalSpaceBytes += sizeBytes;
                    }
                }
                metadata.RequiredSpaceBytes = totalSpaceBytes;
            }

            return metadata;
        }
    }
}
