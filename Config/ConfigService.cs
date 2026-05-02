using System;
using System.IO;
using System.Text.Json;

namespace db_restore_tool
{
    public interface IConfigService
    {
        RestoreConfig LoadConfig();
    }

    public class ConfigService : IConfigService
    {
        private readonly string _configPath;

        public ConfigService()
        {
            string exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
            _configPath = Path.Combine(exeDirectory, "config.json");
        }

        public RestoreConfig LoadConfig()
        {
            if (!File.Exists(_configPath))
            {
                CreateDefaultConfig(_configPath);
                throw new Exception($"Configuration file created at {_configPath}. Please configure it and rerun the application.");
            }

            try
            {
                string configJson = File.ReadAllText(_configPath);
                var config = JsonSerializer.Deserialize<RestoreConfig>(configJson);

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
            var defaultConfig = new RestoreConfig
            {
                ServerName = "RG-PC",
                Username = "sa",
                Password = "Admin@123",
                TempDirectory = @"C:\db_restore_tool\TempDB",
                ZipPassword = new System.Collections.Generic.List<string> { "", "" },
                DataLocation = @"C:\db_restore_tool\MSSQL-DATA"
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(defaultConfig, options);
            File.WriteAllText(path, json);
        }
    }
}
