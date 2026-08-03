# db_restore_tool

A .NET 8 command-line utility for restoring database backups to **Microsoft SQL Server** and **PostgreSQL**, with built-in archive extraction (7-Zip), pre-restore validation, interactive conflict resolution, and standalone query execution.

## Features

- **Multi-server support** — define multiple SQL Server / PostgreSQL instances in a single `config.json`.
- **Archive handling** — restores directly from `.7z`, `.zip`, and `.rar` archives. Bundled 7-Zip binaries are auto-extracted to a temp folder at runtime (no external install needed).
- **Password-protected archives** — tries a list of candidate passwords from config.
- **Disk space validation** — reads backup metadata and verifies sufficient free space before restoring.
- **Conflict resolution** — if the target database exists, prompts to replace, restore under a new name, or cancel (or use `--force-replace` to skip the prompt).
- **MSSQL specifics** — reads logical file names via `RESTORE FILELISTONLY`, moves data/log files to a configurable `DataLocation`, kills active connections, drops the target DB, and streams real-time restore progress (`STATS = 5`).
- **PostgreSQL specifics** — terminates active connections, drops/creates the target database, and shells out to `psql` (for `.sql` dumps) or `pg_restore` (for `.dump` / `.backup` / binary dumps).
- **Query mode** — run a standalone `.sql` file against a database and export every result set to timestamped CSV files.
- **Post-restore query** — optionally runs a verification query after a restore and writes results to a configured output folder.
- **Orphaned file detection** — catches orphaned `.mdf`/`.ldf` files that would cause "Access Denied" on restore.

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) to build/run from source.
- Windows (project is published as `win-x64` self-contained single-file).
- PostgreSQL restore additionally requires `psql` and `pg_restore` on `PATH` (and they must be installed on the machine).
- `.NET 8.0` runtime for the published binary.

## Build

```powershell
dotnet build -c Release
dotnet publish -c Release -r win-x64
```

The project publishes as a self-contained single file:

```xml
<PublishSingleFile>true</PublishSingleFile>
<SelfContained>true</SelfContained>
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
```

The `zip/` folder contents (`7z.exe`, `7z.dll`) are embedded as resources and extracted to `%TEMP%\db_restore_tool\7z\` at runtime.

## Usage

```
db_restore_tool <backup_file> [target_db] [options]
db_restore_tool -q <query_file> -d <target_db> [options]
```

### Options

| Option | Description |
| --- | --- |
| `-s, --server <name>` | Target server from config (defaults to `DefaultServer` in config). |
| `-d, --database <name>` | Target database name (overrides positional argument). |
| `-q, --query <path>` | Run a standalone query instead of a restore. |
| `-o, --output <path>` | Output directory for query results (defaults to `QueryResultsPath`). |
| `-f, --force-replace` | Force replace an existing database without prompting. |
| `-h, --help` | Show help. |

### Examples

Restore a `.bak` to the default server, deriving the database name from the filename:

```powershell
db_restore_tool C:\backups\MyApp.bak
```

Restore an archive to a named server and database, replacing without prompting:

```powershell
db_restore_tool C:\backups\MyApp.7z -s prod-mssql -d MyApp -f
```

Run a query and export CSV results:

```powershell
db_restore_tool -q C:\queries\report.sql -d MyApp -o C:\results
```

If no server is specified (and no `DefaultServer`), an interactive engine/server picker is shown. If a `.sql` file is passed as the positional argument without `-q`, query mode is auto-detected.

## Configuration (`config.json`)

`config.json` lives next to the executable. If missing, a default template is generated and the app exits, telling you to configure it and rerun.

```json
{
  "DefaultServer": "local-mssql",
  "Servers": [
    {
      "Name": "local-mssql",
      "Type": "MSSQL",
      "ServerName": "RG-PC",
      "Username": "sa",
      "Password": "Admin@123",
      "DataLocation": "C:\\DbRestoreTool\\MSSQL-DATA"
    },
    {
      "Name": "dev-postgres",
      "Type": "PostgreSQL",
      "Host": "localhost",
      "Port": 5432,
      "Username": "postgres",
      "Password": "admin123",
      "DataLocation": "C:\\DbRestoreTool\\PG-DATA"
    }
  ],
  "TempDirectory": "C:\\DbRestoreTool\\TempDB",
  "QueryPath": "C:\\DbRestoreTool\\query.sql",
  "QueryResultsPath": "C:\\DbRestoreTool\\Result",
  "ZipPassword": [ "", "" ]
}
```

### Field reference

| Field | Description |
| --- | --- |
| `DefaultServer` | Server used when `-s` is omitted. |
| `Servers[]` | List of server definitions (see below). |
| `TempDirectory` | Root temp folder; each job gets an isolated `GUID` subfolder for extraction. |
| `QueryPath` | Optional SQL file run automatically after a successful restore. |
| `QueryResultsPath` | Default output folder for query/result CSV exports. |
| `ZipPassword` | Candidate passwords tried when extracting protected archives (may be a single string or array). |

### Server fields

| Field | Applies to | Description |
| --- | --- | --- |
| `Name` | both | Unique identifier used by `-s`. |
| `Type` | both | `MSSQL` or `PostgreSQL`. |
| `ServerName` | MSSQL | SQL Server instance (e.g. `RG-PC`). |
| `Host` | PostgreSQL | Postgres hostname/IP. |
| `Port` | PostgreSQL | Postgres port (default 5432 if omitted). |
| `Username` | both | Login (SQL auth for MSSQL, or Windows auth is attempted as fallback). |
| `Password` | both | Password. |
| `DataLocation` | both | Directory where data files land (`*.mdf`/`*.ldf` for MSSQL). |

> **Note:** MSSQL connection tries SQL Authentication first, then Windows Authentication (`IntegratedSecurity=true`). Restore file paths must be visible to the SQL Server service account.

## How It Works

### Restore pipeline

1. **Read config & select server** — parse `config.json`, resolve the target server (flag, default, or interactive picker).
2. **Verify connection** — `EnsureConnectionAsync` against the server.
3. **Validate backup file** — existence + size.
4. **Extract archive** — if input is `.7z`/`.zip`/`.rar`, extract to an isolated temp dir using embedded 7-Zip; locate the restore file (`.bak` for MSSQL; `.sql`/`.dump`/`.backup` for PostgreSQL). Otherwise the file is used directly.
5. **Read backup metadata** — MSSQL uses `RESTORE HEADERONLY` + `RESTORE FILELISTONLY` to get the original database name, logical file names, and required space; PostgreSQL estimates from file size.
6. **Disk space check** — compare required vs. available space on the drive hosting `DataLocation`.
7. **Conflict resolution** — if the database exists, ask `1` (replace) / `2` (new name) / `3` (cancel) unless `-f` is given. For MSSQL, orphaned `.mdf`/`.ldf` files are also detected.
8. **Restore** — kill connections, drop existing DB, then run the restore with live progress (`STATS = 5` percent messages). PostgreSQL creates the DB then runs `psql`/`pg_restore`.
9. **Summary & post-query** — log elapsed time and final size, copy DB name to clipboard, optionally run `QueryPath`.

### Query mode

- Executes the whole `.sql` file against the chosen database with an unlimited command timeout.
- Each result set is exported to `Result_<timestamp>_<id>_<n>.csv` in the output folder (UTF-8, CSV-escaped). Non-result commands report `(n rows affected)`.

## Architecture

| Layer | Location | Responsibility |
| --- | --- | --- |
| Entry point | `Program.cs` | CLI parsing, DI bootstrap, interactive selection, orchestration. |
| Models | `Models/` | `AppConfig`, `ServerConfig`, `BackupMetadata`, `RestoreContext`, `RestoreJob`, JSON converter. |
| Config | `Config/ConfigService.cs` | Load/validate `config.json`, generate default template, detect old format. |
| Common | `Common/` | `DiskSpaceValidator`, `FileSystemService` (filesystem abstraction), `JobLogger`/`ConsoleLogger` (logging + progress bar + prompts + clipboard). |
| Database | `Database/` | `IDatabaseProvider` abstraction, `DatabaseProviderFactory`, and MSSQL / PostgreSQL implementations. |
| Database.Mssql | `Database/Mssql/` | `MssqlConnectionProvider` + `Commands/` (drop, kill connections, restore) + `Queries/` (exists, size, metadata). |
| Database.Postgres | `Database/Postgres/` | `PostgresConnectionProvider` + provider logic incl. `psql`/`pg_restore` invocation. |
| Extraction | `Extraction/` | `ArchiveExtractionService` (7-Zip extraction with password fallback + progress), `SevenZipBootstrapper` (extracts embedded `7z.exe`/`7z.dll`). |
| Restore | `Restore/DatabaseRestoreService.cs` | The 8-step restore orchestration. |
| Services | `Services/QueryExecutorService.cs` | Result-set iteration and CSV export. |

### Key interfaces

- `IDatabaseProvider` — the database abstraction used by the restore service:

```csharp
string ProviderType { get; }
Task EnsureConnectionAsync(CancellationToken);
Task<bool> DatabaseExistsAsync(string dbName, CancellationToken);
Task<double> GetDatabaseSizeMbAsync(string dbName, CancellationToken);
Task<BackupMetadata> GetBackupMetadataAsync(string backupFilePath, CancellationToken);
Task KillConnectionsAsync(string dbName, CancellationToken);
Task DropDatabaseAsync(string dbName, CancellationToken);
Task RestoreDatabaseAsync(string dbName, string backupFilePath, BackupMetadata, Action<string>, CancellationToken);
Task<List<string>> GetDatabasesAsync(CancellationToken);
Task ExecuteQueryAsync(string dbName, string queryFilePath, string outputDir, Action<string>, CancellationToken);
```

- `IConsoleLogger` — logging/progress/prompt surface (thread-safe via a shared lock).
- `IArchiveExtractionService`, `IDiskSpaceValidator`, `IFileSystemService` — swap-friendly service abstractions registered via `Microsoft.Extensions.DependencyInjection`.

## Project Structure

```
.
├── Program.cs                    # Entry point, CLI parsing, DI, orchestration
├── db_restore_tool.csproj        # .NET 8, win-x64 single-file publish
├── db_restore_tool.sln
├── Config/
│   └── ConfigService.cs          # config.json load/validate/generate
├── Models/
│   ├── AppConfig.cs
│   ├── ServerConfig.cs
│   ├── BackupMetadata.cs
│   ├── RestoreContext.cs
│   ├── RestoreJob.cs
│   └── SingleOrArrayConverter.cs
├── Common/
│   ├── ConsoleLogger.cs          # IConsoleLogger + implementation
│   ├── JobLogger.cs              # Thread-safe console logger
│   ├── DiskSpaceValidator.cs
│   └── FileSystemService.cs
├── Database/
│   ├── IDatabaseProvider.cs
│   ├── DatabaseProviderFactory.cs
│   ├── Mssql/
│   │   ├── MssqlConnectionProvider.cs
│   │   ├── MssqlDatabaseProvider.cs
│   │   ├── Commands/  (Drop, KillConnections, Restore)
│   │   └── Queries/   (CheckDatabaseExists, GetBackupMetadata, GetDatabaseSize)
│   └── Postgres/
│       ├── PostgresConnectionProvider.cs
│       └── PostgresDatabaseProvider.cs
├── Extraction/
│   ├── ArchiveExtractionService.cs
│   └── SevenZipBootstrapper.cs
├── Restore/
│   └── DatabaseRestoreService.cs
├── Services/
│   └── QueryExecutorService.cs
└── zip/                          # Embedded 7z.exe / 7z.dll resources
```

## Known Caveats

- `RestoreContext.IsExplicitTargetNameSelected()` always returns `true` (simplified); the metadata database name only overrides the target when the target equals the input file's base name.
- PostgreSQL restore relies on `psql`/`pg_restore` being installed and on `PATH`; the password is passed via the `PGPASSWORD` environment variable.
- `DropDatabaseCommand` silently tolerates error 3 ("operating system error 3") so a `RESTORE ... WITH REPLACE` can overwrite orphaned records.
- Archive extraction only looks in the top directory of the archive for restore files.
- The clipboard copy uses a PowerShell `Set-Clipboard` subprocess (Windows only).
- Older single-server `config.json` format (with a top-level `ServerName`) is detected and rejected — delete the file to regenerate.
