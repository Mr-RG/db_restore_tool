namespace db_restore_tool.Models
{
    public class ServerConfig
    {
        public string Name { get; init; }
        public string Type { get; init; } // "MSSQL" or "PostgreSQL"
        
        // MSSQL properties
        public string ServerName { get; init; }
        
        // PostgreSQL properties
        public string Host { get; init; }
        public int Port { get; init; }

        // Common credentials
        public string Username { get; init; }
        public string Password { get; init; }
        public string DataLocation { get; init; }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Name))
                throw new System.Exception("Server config error: 'Name' is missing.");
            
            if (string.IsNullOrWhiteSpace(Type))
                throw new System.Exception($"Server '{Name}' config error: 'Type' is missing.");

            if (string.IsNullOrWhiteSpace(DataLocation))
                throw new System.Exception($"Server '{Name}' config error: 'DataLocation' is missing.");
        }
    }
}
