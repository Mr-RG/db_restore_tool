# MsSqlRestoreTool - User Guide

## Overview

**MsSqlRestoreTool** is a powerful command-line utility designed to automate the restoration of Microsoft SQL Server databases. It handles not only direct `.bak` files but also automatically extracts compressed `.zip`, `.7z`, and `.rar` archives before restoring. It provides real-time progress bars, disk space validation, and safe conflict resolution.

---

## Prerequisites

- **OS:** Windows Operating System
- **Runtime:** .NET 8.0 Runtime or higher
- **SQL Server:** Accessible locally or over the network with sufficient privileges to restore databases and drop connections.

---

## Configuration (`config.json`)

On the first run, the tool will automatically generate a default `config.json` in the same directory as the executable. You **must** configure this file before running a restore.

```json
{
  "ServerName": "RG-PC",
  "Username": "sa",
  "Password": "Admin@123",
  "TempDirectory": "C:\\mssql_db_restore\\TempDB",
  "ZipPassword": [
    "",
    ""
  ],
  "DataLocation": "C:\\mssql_db_restore\\MSSQL-DATA"
}
```

### Configuration Options:
- **`ServerName`**: The hostname or IP address (and instance name if applicable) of the SQL Server.
- **`Username`**: SQL Server Login user (typically `sa`). *(Windows Authentication is attempted as a fallback if SQL auth fails).*
- **`Password`**: SQL Server Login password.
- **`TempDirectory`**: The scratch directory where `.zip` or `.7z` archives are extracted before the restore. Ensure there is enough free space on this drive.
- **`ZipPassword`**: An array of passwords. If your backup archives are password-protected, the tool will try these passwords sequentially to extract the archive.
- **`DataLocation`**: The target folder where the SQL Server `.mdf` and `.ldf` files will be permanently placed.

---

## Usage Mode

Open your terminal (PowerShell or Command Prompt) and use the following syntax:

```shell
mssql-db-restore <input_file_path> [target_database_name] [options]
```

### Arguments:
- `<input_file_path>` **(Required)**: The absolute or relative path to your backup file (`.bak`, `.zip`, `.7z`).
- `[target_database_name]` *(Optional)*: The name of the database you want to restore to. If omitted, the tool will attempt to read the logical database name from the `.bak` header or intelligently default to the filename.

### Options:
- `-f`, `--force-replace`: Automatically drop and replace any existing database with the target database name without prompting for user confirmation.
- `-h`, `--help`: Prints the help menu and usage instructions.

### Examples:

**1. Standard Restore from a .bak file:**
```shell
mssql-db-restore C:\backups\production_db.bak
```

**2. Restore from a password-protected zip file to a specific database name:**
```shell
mssql-db-restore C:\backups\archive.zip DevDatabase
```

**3. Restore and forcefully overwrite an existing database:**
```shell
mssql-db-restore C:\backups\mydb.bak DevDatabase -f
```

---

## Features & Behaviors

1. **Auto-Extraction:** If you feed the tool a `.zip` or `.7z` file, it will silently extract it in the `TempDirectory` using embedded 7-Zip binaries, locate the `.bak` file inside, restore it, and then clean up the temporary files automatically.
2. **Space Validation:** Before performing a heavy restore, the tool checks the required sizes from the backup headers and compares it against your target `DataLocation`'s free disk space.
3. **Conflict Handling:** If a database already exists with your target name, the tool will interactively prompt you. You can choose to proceed and overwrite it, rename the target, or cancel. (Unless `-f` is passed).
4. **Active Connection Killing:** If overwriting an existing database, the tool will safely kill active connections to ensure the drop-and-restore process is not blocked.
5. **Progress Tracking:** Shows real-time percentage progress bars for both archive extraction and SQL Server database restoration.
