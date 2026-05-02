using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace db_restore_tool
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
            {
                PrintHelp();
                return 0;
            }

            string inputFilePath = args[0];
            string targetDatabaseName = args.Length > 1 && !args[1].StartsWith("-") ? args[1] : null;
            bool forceReplace = args.Contains("-f") || args.Contains("--force-replace");

            var configService = new ConfigService();
            RestoreConfig config;
            try
            {
                config = configService.LoadConfig();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"ERROR: {ex.Message}");
                Console.ResetColor();
                return -1;
            }

            try
            {
                var services = new ServiceCollection();
                services.AddSingleton(config);
                services.AddSingleton<IConsoleLogger, ConsoleLogger>();
                services.AddSingleton<IDiskSpaceValidator, DiskSpaceValidator>();
                
                // Register Extraction Services
                services.AddSingleton<ISevenZipBootstrapper, SevenZipBootstrapper>();
                services.AddSingleton<IArchiveExtractionService, ArchiveExtractionService>();
                services.AddSingleton<IFileSystemService, FileSystemService>();

                // Register SQL CQRS
                services.AddSingleton<db_restore_tool.Database.IConnectionProvider, db_restore_tool.Database.ConnectionProvider>();
                services.AddSingleton<db_restore_tool.Database.Queries.CheckDatabaseExistsQuery>();
                services.AddSingleton<db_restore_tool.Database.Queries.GetDatabaseSizeQuery>();
                services.AddSingleton<db_restore_tool.Database.Queries.GetBackupMetadataQuery>();
                services.AddSingleton<db_restore_tool.Database.Commands.KillConnectionsCommand>();
                services.AddSingleton<db_restore_tool.Database.Commands.DropDatabaseCommand>();
                services.AddSingleton<db_restore_tool.Database.Commands.RestoreDatabaseCommand>();
                
                services.AddSingleton<DatabaseRestoreService>();

                await using var serviceProvider = services.BuildServiceProvider();
                var restoreService = serviceProvider.GetRequiredService<DatabaseRestoreService>();

                using var cts = new CancellationTokenSource();
                Console.CancelKeyPress += (s, e) =>
                {
                    e.Cancel = true;
                    cts.Cancel();
                };

                var context = new RestoreContext(inputFilePath, targetDatabaseName, forceReplace);
                await restoreService.ExecuteRestoreAsync(context, cts.Token);

                return 0;
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("\nOperation was cancelled by the user.");
                return 1;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\nFATAL ERROR: {ex.Message}");
                Console.ResetColor();
                return -1;
            }
        }

        static void PrintHelp()
        {
            Console.WriteLine("MsSqlRestoreTool");
            Console.WriteLine("Usage:");
            Console.WriteLine("  mssql-db-restore <input_file_path> [target_database_name] [options]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  -f, --force-replace   Force replace existing database without prompting");
            Console.WriteLine("  -h, --help            Show help and usage information");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine(@"  mssql-db-restore C:\backups\mydb.bak");
            Console.WriteLine(@"  mssql-db-restore C:\backups\mydb.bak DevDatabase -f");
        }
    }
}