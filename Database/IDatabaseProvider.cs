using System;
using System.Threading;
using System.Threading.Tasks;
using db_restore_tool.Models;

namespace db_restore_tool.Database
{
    public interface IDatabaseProvider
    {
        string ProviderType { get; }

        Task EnsureConnectionAsync(CancellationToken cancellationToken = default);
        Task<bool> DatabaseExistsAsync(string dbName, CancellationToken cancellationToken = default);
        Task<double> GetDatabaseSizeMbAsync(string dbName, CancellationToken cancellationToken = default);
        Task<BackupMetadata> GetBackupMetadataAsync(string backupFilePath, CancellationToken cancellationToken = default);
        Task KillConnectionsAsync(string dbName, CancellationToken cancellationToken = default);
        Task DropDatabaseAsync(string dbName, CancellationToken cancellationToken = default);
        Task RestoreDatabaseAsync(string dbName, string backupFilePath, BackupMetadata metadata, Action<string> messageCallback, CancellationToken cancellationToken = default);

        Task<System.Collections.Generic.List<string>> GetDatabasesAsync(CancellationToken cancellationToken = default);
        Task ExecuteQueryAsync(string dbName, string queryFilePath, string outputDir, Action<string> messageCallback, CancellationToken cancellationToken = default);
    }
}
