using System.Collections.Generic;

namespace db_restore_tool.Models
{
    public class AppConfig
    {
        public string DefaultServer { get; init; }
        public List<ServerConfig> Servers { get; init; } = new List<ServerConfig>();
        public string TempDirectory { get; init; }
        
        public string QueryPath { get; init; }
        public string QueryResultsPath { get; init; }
        
        [System.Text.Json.Serialization.JsonConverter(typeof(SingleOrArrayConverter<string>))]
        public List<string> ZipPassword { get; init; } = new List<string>();

        public ServerConfig GetServer(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                name = DefaultServer;
            }

            var server = Servers.Find(s => s.Name.Equals(name, System.StringComparison.OrdinalIgnoreCase));
            if (server == null)
            {
                throw new System.Exception($"Server '{name}' not found in configuration.");
            }
            return server;
        }

        public void Validate()
        {
            if (Servers == null || Servers.Count == 0)
                throw new System.Exception("Config error: No servers defined.");
        }
    }
}
