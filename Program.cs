using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using db_restore_tool.Config;
using db_restore_tool.Models;
using db_restore_tool.Database;
using db_restore_tool.Common;
using db_restore_tool.Restore;

namespace db_restore_tool
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            if (args.Length == 0 || args[0].Equals("--help", StringComparison.OrdinalIgnoreCase) || args[0].Equals("-h", StringComparison.OrdinalIgnoreCase))
            {
                PrintHelp();
                return 0;
            }

            string inputFilePath = null;
            string targetDatabaseName = null;
            string serverName = null;
            string queryFilePath = null;
            string outputPath = null;
            bool forceReplace = false;
            bool isQueryMode = false;

            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i].ToLowerInvariant();
                if (arg == "-f" || arg == "--force-replace")
                    forceReplace = true;
                else if ((arg == "-s" || arg == "--server") && i + 1 < args.Length)
                    serverName = args[++i];
                else if ((arg == "-q" || arg == "--query") && i + 1 < args.Length)
                {
                    isQueryMode = true;
                    queryFilePath = args[++i];
                }
                else if ((arg == "-d" || arg == "--database") && i + 1 < args.Length)
                    targetDatabaseName = args[++i];
                else if ((arg == "-o" || arg == "--output") && i + 1 < args.Length)
                    outputPath = args[++i];
                else if (!args[i].StartsWith("-"))
                {
                    if (inputFilePath == null) inputFilePath = args[i];
                    else if (targetDatabaseName == null) targetDatabaseName = args[i];
                }
            }

            // Fallback for query mode if the input file ends with .sql and -q was not explicitly provided
            if (!isQueryMode && queryFilePath == null && inputFilePath != null && inputFilePath.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            {
                isQueryMode = true;
                queryFilePath = inputFilePath;
                inputFilePath = null;
            }

            var configService = new ConfigService();
            AppConfig config;
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

            ServerConfig targetServer = null;
            if (string.IsNullOrWhiteSpace(serverName))
            {
                serverName = config.DefaultServer;
            }

            if (string.IsNullOrWhiteSpace(serverName) || serverName.Equals("null", StringComparison.OrdinalIgnoreCase))
            {
                // Interactive Engine/Server selection
                Console.WriteLine("\n--- Server Selection ---");
                Console.WriteLine("Select Engine Type:");
                var engineTypes = config.Servers.Select(s => s.Type).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                for (int j = 0; j < engineTypes.Count; j++) Console.WriteLine($"{j + 1}. {engineTypes[j]}");
                Console.Write("> ");
                
                if (int.TryParse(Console.ReadLine(), out int typeIndex) && typeIndex > 0 && typeIndex <= engineTypes.Count)
                {
                    string selectedType = engineTypes[typeIndex - 1];
                    var serversOfType = config.Servers.Where(s => s.Type.Equals(selectedType, StringComparison.OrdinalIgnoreCase)).ToList();
                    
                    Console.WriteLine("\nSelect Server:");
                    for (int j = 0; j < serversOfType.Count; j++) Console.WriteLine($"{j + 1}. {serversOfType[j].Name} ({serversOfType[j].Host ?? serversOfType[j].ServerName})");
                    Console.Write("> ");
                    
                    if (int.TryParse(Console.ReadLine(), out int serverIndex) && serverIndex > 0 && serverIndex <= serversOfType.Count)
                    {
                        targetServer = serversOfType[serverIndex - 1];
                    }
                }
            }
            else
            {
                try
                {
                    targetServer = config.GetServer(serverName);
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"ERROR: {ex.Message}");
                    Console.ResetColor();
                    return -1;
                }
            }

            if (targetServer == null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("ERROR: No valid server selected.");
                Console.ResetColor();
                return -1;
            }

            try
            {
                var services = new ServiceCollection();
                services.AddSingleton(config);
                services.AddSingleton<IDiskSpaceValidator, DiskSpaceValidator>();
                services.AddSingleton<ISevenZipBootstrapper, SevenZipBootstrapper>();
                services.AddSingleton<IArchiveExtractionService, ArchiveExtractionService>();
                services.AddSingleton<IFileSystemService, FileSystemService>();
                services.AddSingleton<DatabaseProviderFactory>();

                await using var serviceProvider = services.BuildServiceProvider();
                
                var factory = serviceProvider.GetRequiredService<DatabaseProviderFactory>();
                var dbProvider = factory.Create(targetServer);

                using var cts = new CancellationTokenSource();
                Console.CancelKeyPress += (s, e) =>
                {
                    e.Cancel = true;
                    cts.Cancel();
                };

                object consoleLock = new object();
                var logger = new JobLogger(targetServer.Name, consoleLock);

                if (isQueryMode)
                {
                    if (string.IsNullOrWhiteSpace(targetDatabaseName))
                    {
                        Console.WriteLine("\n--- Database Selection ---");
                        Console.WriteLine("Loading databases...");
                        var dbs = await dbProvider.GetDatabasesAsync(cts.Token);
                        
                        for (int j = 0; j < dbs.Count; j++) Console.WriteLine($"{j + 1}. {dbs[j]}");
                        Console.Write("> ");
                        
                        if (int.TryParse(Console.ReadLine(), out int dbIndex) && dbIndex > 0 && dbIndex <= dbs.Count)
                        {
                            targetDatabaseName = dbs[dbIndex - 1];
                        }
                        else
                        {
                            throw new Exception("Invalid database selection.");
                        }
                    }

                    if (string.IsNullOrWhiteSpace(outputPath))
                    {
                        outputPath = config.QueryResultsPath;
                    }

                    logger.LogHeader();
                    logger.LogStep(1, $"Executing Standalone Query on '{targetDatabaseName}'...");
                    
                    try 
                    {
                        await dbProvider.ExecuteQueryAsync(targetDatabaseName, queryFilePath, outputPath, msg => {
                            Console.WriteLine(msg);
                        }, cts.Token);
                        logger.LogStatus("Query execution completed successfully.");
                        return 0;
                    } 
                    catch(Exception ex) 
                    {
                        logger.LogError(ex.Message);
                        return 1;
                    }
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(inputFilePath))
                    {
                        Console.WriteLine("ERROR: Backup file path is required for restore operation.");
                        PrintHelp();
                        return -1;
                    }

                    var restoreService = new DatabaseRestoreService(
                        dbProvider,
                        logger,
                        serviceProvider.GetRequiredService<IDiskSpaceValidator>(),
                        serviceProvider.GetRequiredService<IArchiveExtractionService>(),
                        serviceProvider.GetRequiredService<IFileSystemService>()
                    );

                    var context = new RestoreContext(inputFilePath, targetDatabaseName, forceReplace);
                    
                    var job = new RestoreJob
                    {
                        Config = config,
                        Server = targetServer,
                        Context = context,
                        IsolatedTempDir = Path.Combine(config.TempDirectory, Guid.NewGuid().ToString("N"))
                    };

                    await restoreService.ExecuteRestoreAsync(job, cts.Token);

                    return job.Status == JobStatus.Completed ? 0 : 1;
                }
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
            Console.WriteLine("db_restore_tool");
            Console.WriteLine("Usage (Restore):");
            Console.WriteLine("  db_restore_tool <backup_file> [target_db] [options]");
            Console.WriteLine("Usage (Query):");
            Console.WriteLine("  db_restore_tool -q <query_file> -d <target_db> [options]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  -s, --server <name>   Specify target server from config (default: 'DefaultServer')");
            Console.WriteLine("  -d, --database <name> Specify target database (overrides positional argument)");
            Console.WriteLine("  -q, --query <path>    Run a standalone query instead of restoring");
            Console.WriteLine("  -o, --output <path>   Output directory for query results (default: QueryResultsPath in config)");
            Console.WriteLine("  -f, --force-replace   Force replace existing database without prompting");
            Console.WriteLine("  -h, --help            Show help and usage information");
        }
    }
}