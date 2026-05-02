using System;
using System.IO;
using System.Text.Json;
using db_restore_tool.Models;

namespace db_restore_tool.Config
{
    public interface IConfigService
    {
        AppConfig LoadConfig();
    }

    public class ConfigService : IConfigService
    {
        private readonly string _configPath;

        public ConfigService()
        {
            string exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
            _configPath = Path.Combine(exeDirectory, "config.json");
        }

        public AppConfig LoadConfig()
        {
            if (!File.Exists(_configPath))
            {
                CreateDefaultConfig(_configPath);
                throw new Exception($"Configuration file created at {_configPath}. Please configure it and rerun the application.");
            }

            try
            {
                string configJson = File.ReadAllText(_configPath);
                
                // Add auto-migration logic here if it's the old format? 
                // Since this is a new feature we'll just parse the new format.
                // We could use JsonDocument to detect old structure.
                using (var doc = JsonDocument.Parse(configJson))
                {
                    if (doc.RootElement.TryGetProperty("ServerName", out _))
                    {
                        // Old format detected
                        throw new Exception("Old configuration format detected. Please delete config.json and let the application generate a new one, or manually update it to the new multi-server format.");
                    }
                }

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                var config = JsonSerializer.Deserialize<AppConfig>(configJson, options);

                if (config == null)
                    throw new Exception("Configuration is null.");

                config.Validate();

                return config;
            }
            catch (JsonException ex)
            {
                throw new Exception($"Failed to parse config.json: {ex.Message}");
            }
        }

        private void CreateDefaultConfig(string path)
        {
            var defaultConfig = new AppConfig
            {
                DefaultServer = "local-mssql",
                Servers = new System.Collections.Generic.List<ServerConfig>
                {
                    new ServerConfig
                    {
                        Name = "local-mssql",
                        Type = "MSSQL",
                        ServerName = "RG-PC",
                        Username = "sa",
                        Password = "Admin@123",
                        DataLocation = @"C:\DbRestoreTool\MSSQL-DATA"
                    },
                    new ServerConfig
                    {
                        Name = "dev-postgres",
                        Type = "PostgreSQL",
                        Host = "localhost",
                        Port = 5432,
                        Username = "postgres",
                        Password = "admin123",
                        // DataLocation = @"C:\db_restore_tool\PG-DATA"
                    }
                },
                TempDirectory = @"C:\DbRestoreTool\TempDB",
                QueryPath = @"C:\DbRestoreTool\query.sql",
                QueryResultsPath = @"C:\DbRestoreTool\Result",
                ZipPassword = new System.Collections.Generic.List<string> { "", "" }
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(defaultConfig, options);
            File.WriteAllText(path, json);
        }
    }
}
