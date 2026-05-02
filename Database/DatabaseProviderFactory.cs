using System;
using db_restore_tool.Models;
using db_restore_tool.Database.Mssql;
using db_restore_tool.Database.Postgres;

namespace db_restore_tool.Database
{
    public class DatabaseProviderFactory
    {
        public IDatabaseProvider Create(ServerConfig serverConfig)
        {
            if (serverConfig == null) throw new ArgumentNullException(nameof(serverConfig));

            return serverConfig.Type.ToUpperInvariant() switch
            {
                "MSSQL" => new MssqlDatabaseProvider(serverConfig),
                "POSTGRESQL" => new PostgresDatabaseProvider(serverConfig),
                _ => throw new NotSupportedException($"Database type '{serverConfig.Type}' is not supported.")
            };
        }
    }
}
