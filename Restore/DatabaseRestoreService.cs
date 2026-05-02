using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using db_restore_tool.Database;
using db_restore_tool.Models;

namespace db_restore_tool.Restore
{
    public class DatabaseRestoreService
    {
        private readonly IDatabaseProvider _databaseProvider;
        private readonly IConsoleLogger _logger;
        private readonly IDiskSpaceValidator _diskSpaceValidator;
        private readonly IArchiveExtractionService _archiveExtractionService;
        private readonly IFileSystemService _fileSystem;

        public DatabaseRestoreService(
            IDatabaseProvider databaseProvider,
            IConsoleLogger logger,
            IDiskSpaceValidator diskSpaceValidator,
            IArchiveExtractionService archiveExtractionService,
            IFileSystemService fileSystem)
        {
            _databaseProvider = databaseProvider;
            _logger = logger;
            _diskSpaceValidator = diskSpaceValidator;
            _archiveExtractionService = archiveExtractionService;
            _fileSystem = fileSystem;
        }

        public async Task ExecuteRestoreAsync(RestoreJob job, CancellationToken cancellationToken)
        {
            var context = job.Context;
            var server = job.Server;

            _logger.LogHeader();
            job.Status = JobStatus.Extracting;

            // STEP 1: Configuration
            _logger.LogStep(1, "Reading configuration...");
            _logger.LogDetail("Server Type", server.Type);
            _logger.LogDetail("Server Name", server.Type == "MSSQL" ? server.ServerName : server.Host);
            if (!string.IsNullOrEmpty(server.DataLocation))
            {
                _logger.LogDetail("Data Location", server.DataLocation);
            }
            _logger.LogStatus("OK");

            // STEP 2: Verify Connection
            _logger.LogStep(2, $"Verifying {server.Type} connection...");
            try
            {
                await _databaseProvider.EnsureConnectionAsync(cancellationToken);
                _logger.LogStatus("Connected");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                job.Status = JobStatus.Failed;
                return;
            }

            // STEP 3: Validate input backup file
            _logger.LogStep(3, "Checking backup file...");
            if (!_fileSystem.FileExists(context.InputFilePath))
            {
                _logger.LogError($"Input file not found: {context.InputFilePath}");
                job.Status = JobStatus.Failed;
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
                    context.BakFilePath = await _archiveExtractionService.ExtractArchiveAsync(
                        context.InputFilePath, 
                        job.IsolatedTempDir, 
                        server.Type,
                        percent => { _logger.RenderProgressBar(percent); }, 
                        cancellationToken);
                    
                    context.ExtractionTempDir = job.IsolatedTempDir;
                    _logger.LogStatus("Success");
                }
                catch (Exception ex)
                {
                    _logger.ClearProgressBar();
                    _logger.LogError($"Extraction failed: {ex.Message}");
                    job.Status = JobStatus.Failed;
                    return;
                }
            }
            else
            {
                _logger.LogStatus("Skipped (Not an archive)");
            }

            try
            {
                job.Status = JobStatus.Restoring;

                // Retrieve Metadata
                var metadata = await _databaseProvider.GetBackupMetadataAsync(context.BakFilePath, cancellationToken);
                
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

                if (_diskSpaceValidator.HasEnoughSpace(server.DataLocation, metadata.RequiredSpaceBytes, out long availableSpaceBytes))
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
                    job.Status = JobStatus.Failed;
                    return;
                }

                // STEP 6: Check existing database & Conflict Resolution
                _logger.LogStep(6, "Checking existing database...");
                bool dbExists = await _databaseProvider.DatabaseExistsAsync(context.TargetDatabaseName, cancellationToken);
                
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
                            job.Status = JobStatus.Failed;
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
                            dbExists = await _databaseProvider.DatabaseExistsAsync(context.TargetDatabaseName, cancellationToken);
                        }
                    }
                }
                else
                {
                    _logger.LogStatus($"Database '{context.TargetDatabaseName}' does not exist. Proceeding with restore.");
                }

                // Check for orphaned MDF/LDF files if MSSQL
                if (server.Type.ToUpperInvariant() == "MSSQL" && !dbExists)
                {
                    string dataFileName = Path.Combine(server.DataLocation, $"{context.TargetDatabaseName}.mdf");
                    string logFileName = Path.Combine(server.DataLocation, $"{context.TargetDatabaseName}_log.ldf");

                    if (_fileSystem.FileExists(dataFileName) || _fileSystem.FileExists(logFileName))
                    {
                        _logger.LogError($"Target database files already exist but the database does not (Orphaned Files).\nData File: {dataFileName}\nLog File: {logFileName}\nSQL Server will fail with 'Access Denied'. Please rename the target database or delete these files manually.");
                        job.Status = JobStatus.Failed;
                        return;
                    }
                }

                // STEP 7: Restore
                _logger.LogStep(7, $"Restoring database '{context.TargetDatabaseName}'...");
                
                if (dbExists)
                {
                    _logger.LogStatus($"Dropping existing database '{context.TargetDatabaseName}'...");
                    try 
                    {
                        await _databaseProvider.KillConnectionsAsync(context.TargetDatabaseName, cancellationToken);
                        await _databaseProvider.DropDatabaseAsync(context.TargetDatabaseName, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Initial drop failed: {ex.Message}. Attempting to overwrite via RESTORE WITH REPLACE...");
                    }
                }

                var stopwatch = Stopwatch.StartNew();
                try
                {
                    await _databaseProvider.RestoreDatabaseAsync(context.TargetDatabaseName, context.BakFilePath, metadata, message =>
                    {
                        if (server.Type.ToUpperInvariant() == "MSSQL" && message.IndexOf("percent processed", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            var parts = message.Trim().Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length > 0 && int.TryParse(parts[0], out int percent))
                            {
                                _logger.RenderProgressBar(percent);
                            }
                        }
                        else if (server.Type.ToUpperInvariant() == "POSTGRESQL")
                        {
                            // Postgres doesn't give clean percentages usually, but we can log verbose if needed
                            // For now, we'll just let it run.
                        }
                    }, cancellationToken);

                    _logger.LogStatus("Success");
                }
                catch (Exception ex)
                {
                    _logger.ClearProgressBar();
                    _logger.LogError($"Restore failed: {ex.Message}");
                    job.Status = JobStatus.Failed;
                    return;
                }
                stopwatch.Stop();

                double dbSizeMb = await _databaseProvider.GetDatabaseSizeMbAsync(context.TargetDatabaseName, cancellationToken);
                
                var timeString = stopwatch.Elapsed.TotalMinutes >= 1
                    ? $"{stopwatch.Elapsed.TotalMinutes:F3} minutes"
                    : $"{stopwatch.Elapsed.TotalSeconds:F3} seconds";

                _logger.LogSummary(context.TargetDatabaseName, _diskSpaceValidator.FormatSize((long)(dbSizeMb * 1024 * 1024)), timeString);
                await _logger.CopyToClipboardAsync(context.TargetDatabaseName);

                // STEP 8: Post-Restore Query
                if (!string.IsNullOrWhiteSpace(job.Config.QueryPath) && _fileSystem.FileExists(job.Config.QueryPath))
                {
                    _logger.LogStep(8, "Executing Query...");
                    try 
                    {
                        await _databaseProvider.ExecuteQueryAsync(context.TargetDatabaseName, job.Config.QueryPath, job.Config.QueryResultsPath, msg => {
                            _logger.LogDetail("Query Output", msg);
                        }, cancellationToken);
                        _logger.LogStatus("Query executed successfully.");
                    } 
                    catch (Exception ex) 
                    {
                        _logger.LogWarning($"Post-Restore Query failed: {ex.Message}");
                    }
                }
                
                job.Status = JobStatus.Completed;
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
