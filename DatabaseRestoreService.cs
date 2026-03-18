using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using mssql_db_restore.Sql;
using mssql_db_restore.Sql.Queries;
using mssql_db_restore.Sql.Commands;

namespace mssql_db_restore
{
    public class DatabaseRestoreService
    {
        private readonly RestoreConfig _config;
        private readonly IConnectionProvider _connectionProvider;
        private readonly CheckDatabaseExistsQuery _checkDatabaseExistsQuery;
        private readonly GetDatabaseSizeQuery _getDatabaseSizeQuery;
        private readonly GetBackupMetadataQuery _getBackupMetadataQuery;
        private readonly KillConnectionsCommand _killConnectionsCommand;
        private readonly DropDatabaseCommand _dropDatabaseCommand;
        private readonly RestoreDatabaseCommand _restoreDatabaseCommand;
        
        private readonly IConsoleLogger _logger;
        private readonly IDiskSpaceValidator _diskSpaceValidator;
        private readonly IArchiveExtractionService _archiveExtractionService;
        private readonly IFileSystemService _fileSystem;

        public DatabaseRestoreService(
            RestoreConfig config,
            IConnectionProvider connectionProvider,
            CheckDatabaseExistsQuery checkDatabaseExistsQuery,
            GetDatabaseSizeQuery getDatabaseSizeQuery,
            GetBackupMetadataQuery getBackupMetadataQuery,
            KillConnectionsCommand killConnectionsCommand,
            DropDatabaseCommand dropDatabaseCommand,
            RestoreDatabaseCommand restoreDatabaseCommand,
            IConsoleLogger logger,
            IDiskSpaceValidator diskSpaceValidator,
            IArchiveExtractionService archiveExtractionService,
            IFileSystemService fileSystem)
        {
            _config = config;
            _connectionProvider = connectionProvider;
            _checkDatabaseExistsQuery = checkDatabaseExistsQuery;
            _getDatabaseSizeQuery = getDatabaseSizeQuery;
            _getBackupMetadataQuery = getBackupMetadataQuery;
            _killConnectionsCommand = killConnectionsCommand;
            _dropDatabaseCommand = dropDatabaseCommand;
            _restoreDatabaseCommand = restoreDatabaseCommand;
            
            _logger = logger;
            _diskSpaceValidator = diskSpaceValidator;
            _archiveExtractionService = archiveExtractionService;
            _fileSystem = fileSystem;
        }

        public async Task ExecuteRestoreAsync(RestoreContext context, CancellationToken cancellationToken)
        {
            _logger.LogHeader();

            // STEP 1: Configuration
            _logger.LogStep(1, "Reading configuration...");
            _logger.LogDetail("Server Name", _config.ServerName);
            _logger.LogStatus("OK");

            // STEP 2: Verify SQL Server connection
            _logger.LogStep(2, "Verifying SQL Server connection...");
            try
            {
                await _connectionProvider.EnsureConnectionAsync(cancellationToken);
                _logger.LogStatus("Connected");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                return;
            }

            // STEP 3: Validate input backup file
            _logger.LogStep(3, "Checking backup file...");
            if (!_fileSystem.FileExists(context.InputFilePath))
            {
                _logger.LogError($"Input file not found: {context.InputFilePath}");
                return;
            }
            
            long fileSizeBytes = _fileSystem.GetFileSize(context.InputFilePath);
            _logger.LogDetail("File Name", Path.GetFileName(context.InputFilePath));
            _logger.LogDetail("File Size", _diskSpaceValidator.FormatSize(fileSizeBytes));
            _logger.LogStatus("OK");

            context.BakFilePath = context.InputFilePath;

            // STEP 4: Extract archive
            _logger.LogStep(4, "Extracting archive...");
            if (_archiveExtractionService.IsArchive(context.InputFilePath))
            {
                try
                {
                    context.BakFilePath = await _archiveExtractionService.ExtractArchiveAsync(context.InputFilePath, percent =>
                    {
                        _logger.RenderProgressBar(percent);
                    }, cancellationToken);
                    context.ExtractionTempDir = Path.GetDirectoryName(context.BakFilePath);
                    _logger.LogStatus("Success");
                }
                catch (Exception ex)
                {
                    _logger.ClearProgressBar();
                    _logger.LogError($"Extraction failed: {ex.Message}");
                    return;
                }
            }
            else
            {
                _logger.LogStatus("Skipped (Not an archive)");
            }

            try
            {
                // Retrieve Metadata from BAK file
                var metadata = await _getBackupMetadataQuery.ExecuteAsync(context.BakFilePath, cancellationToken);
                
                // Deriving target database name if not explicitly set
                if (!context.IsExplicitTargetNameSelected() || context.TargetDatabaseName == Path.GetFileNameWithoutExtension(context.InputFilePath))
                {
                    if (!string.IsNullOrEmpty(metadata.DatabaseName))
                    {
                        context.UpdateTargetDatabaseName(metadata.DatabaseName);
                    }
                }

                // STEP 5: Check required disk space
                _logger.LogStep(5, "Checking disk space...");

                if (metadata.RequiredSpaceBytes > 0)
                {
                    _logger.LogDetail("Required Space", _diskSpaceValidator.FormatSize(metadata.RequiredSpaceBytes));
                }

                if (_diskSpaceValidator.HasEnoughSpace(_config.DataLocation, metadata.RequiredSpaceBytes, out long availableSpaceBytes))
                {
                    if (metadata.RequiredSpaceBytes > 0)
                    {
                        _logger.LogDetail("Available Space", _diskSpaceValidator.FormatSize(availableSpaceBytes));
                        _logger.LogStatus("Sufficient");
                    }
                    else
                    {
                        _logger.LogStatus("Skipped");
                    }
                }
                else
                {
                    _logger.LogDetail("Available Space", _diskSpaceValidator.FormatSize(availableSpaceBytes));
                    _logger.LogError("Insufficient disk space available on the target drive.");
                    return;
                }

                // STEP 6: Check existing database & Conflict Resolution
                _logger.LogStep(6, "Checking existing database...");
                bool dbExists = await _checkDatabaseExistsQuery.ExecuteAsync(context.TargetDatabaseName, cancellationToken);
                
                if (dbExists)
                {
                    _logger.LogWarning($"Database '{context.TargetDatabaseName}' already exists.");

                    if (context.ForceReplace)
                    {
                         _logger.LogStatus("Force replace enabled. Replacing existing database...");
                    }
                    else
                    {
                        string action = _logger.PromptForAction(
                            "Choose an action:\n1 - Replace existing database\n2 - Restore with new database name\n3 - Cancel operation\n>", 
                            new[] { "1", "2", "3" });

                        if (action == "3")
                        {
                            _logger.LogError("Operation was cancelled by the user.");
                            return;
                        }
                        else if (action == "1")
                        {
                            _logger.LogStatus("Continuing to replace existing database...");
                        }
                        else if (action == "2")
                        {
                            var newName = _logger.PromptForString("Enter new database name:");
                            if (string.IsNullOrWhiteSpace(newName))
                            {
                                throw new OperationCanceledException("Invalid database name. Operation canceled.");
                            }
                            context.UpdateTargetDatabaseName(newName);
                            _logger.LogStatus($"Proceeding with new name: {context.TargetDatabaseName}");
                        }
                    }
                }
                else
                {
                    _logger.LogStatus($"Database '{context.TargetDatabaseName}' does not exist. Proceeding with restore.");
                }

                // Check for orphaned MDF/LDF files
                string dataFileName = Path.Combine(_config.DataLocation, $"{context.TargetDatabaseName}.mdf");
                string logFileName = Path.Combine(_config.DataLocation, $"{context.TargetDatabaseName}_log.ldf");
                dbExists = await _checkDatabaseExistsQuery.ExecuteAsync(context.TargetDatabaseName, cancellationToken);

                if (!dbExists)
                {
                    if (_fileSystem.FileExists(dataFileName) || _fileSystem.FileExists(logFileName))
                    {
                        _logger.LogError($"Target database files already exist but the database does not (Orphaned Files).\nData File: {dataFileName}\nLog File: {logFileName}\nSQL Server will fail with 'Access Denied'. Please rename the target database or delete these files manually.");
                        return;
                    }
                }

                // STEP 7: Restore
                _logger.LogStep(7, $"Restoring database '{context.TargetDatabaseName}'...");
                
                if (dbExists)
                {
                    _logger.LogStatus($"Dropping existing database '{context.TargetDatabaseName}'...");
                    await _killConnectionsCommand.ExecuteAsync(context.TargetDatabaseName, cancellationToken);
                    await _dropDatabaseCommand.ExecuteAsync(context.TargetDatabaseName, cancellationToken);
                }

                var stopwatch = Stopwatch.StartNew();
                try
                {
                    await _restoreDatabaseCommand.ExecuteAsync(context.TargetDatabaseName, context.BakFilePath, metadata, message =>
                    {
                        if (message.IndexOf("percent processed", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            var parts = message.Trim().Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length > 0 && int.TryParse(parts[0], out int percent))
                            {
                                _logger.RenderProgressBar(percent);
                            }
                        }
                    }, cancellationToken);

                    _logger.LogStatus("Success");
                }
                catch (Exception ex)
                {
                    _logger.ClearProgressBar();
                    _logger.LogError($"Restore failed: {ex.Message}");
                    return;
                }
                stopwatch.Stop();

                double dbSizeMb = await _getDatabaseSizeQuery.ExecuteAsync(context.TargetDatabaseName, cancellationToken);
                
                var timeString = stopwatch.Elapsed.TotalMinutes >= 1
                    ? $"{stopwatch.Elapsed.TotalMinutes:F3} minutes"
                    : $"{stopwatch.Elapsed.TotalSeconds:F3} seconds";

                _logger.LogSummary(context.TargetDatabaseName, _diskSpaceValidator.FormatSize((long)(dbSizeMb * 1024 * 1024)), timeString);
                await _logger.CopyToClipboardAsync(context.TargetDatabaseName);
            }
            finally
            {
                if (context.ExtractionTempDir != null)
                {
                    _archiveExtractionService.CleanupTempDirectory(context.ExtractionTempDir);
                }
            }
        }
    }
}
