# MsSqlRestoreTool - Developer Guide

## Overview
**MsSqlRestoreTool** is built entirely on .NET 8 using modern C# principles. It adheres rigidly to Object-Oriented Programming (OOP), SOLID principles, and Command/Query Responsibility Segregation (CQRS) for managing its interactions with SQL Server.

---

## Architecture & Code Organization

The solution is divided into two primary projects:
1. **`MsSqlRestoreTool`**: The core application logic.
2. **`MsSqlRestoreTool.Tests`**: The xUnit test suite with Moq framework integration.

### Core Patterns Used

#### 1. Command/Query Responsibility Segregation (CQRS)
SQL interactions are split cleanly between Queries (Reading State) and Commands (Mutating State). The original monolithic abstraction was removed to allow single-responsibility classes.
- **Queries (`Sql/Queries/`):** Evaluate database state without side effects.
  - `CheckDatabaseExistsQuery`: Connects to `master` to determine if a DB name exists.
  - `GetDatabaseSizeQuery`: Calculates target size of a database.
  - `GetBackupMetadataQuery`: Runs `RESTORE HEADERONLY` and `RESTORE FILELISTONLY` to safely extract expected sizes and logical file groups before executing a restore.
- **Commands (`Sql/Commands/`):** Execute state-altering actions.
  - `DropDatabaseCommand`: Drops a database.
  - `KillConnectionsCommand`: Sets DB to single-user mode to kill active query sessions.
  - `RestoreDatabaseCommand`: Executes the core `RESTORE DATABASE` SQL command.

#### 2. Encapsulation & Parameter Objects
Instead of passing 5+ parameters across orchestration layers, state is bundled into contextual objects.
- **`RestoreConfig`**: Immutable configuration object (`init` properties enforced). Exposes a `.Validate()` method.
- **`RestoreContext`**: A parameter-object encapsulating primitive orchestration flags (`InputFilePath`, `TargetDatabaseName`, `ForceReplace`).

#### 3. Separation of Concerns & Dependency Injection
Microsoft's `IServiceCollection` maps all singletons. The application execution logic heavily uses decoupled dependencies:
- `IArchiveExtractionService`: Exclusively manages routing extraction rules.
- `ISevenZipBootstrapper`: Extracts the embedded `.exe` and `.dll` 7-Zip binaries into a temporary path for the host OS to use.
- `IFileSystemService`: Wraps disk interactions (`File.Exists`, `GetFileSize`) allowing the filesystem to be easily mocked in unit tests.
- `IConsoleLogger`: Isolates `Console.WriteLine` rendering away from the domain.

---

## Core Flow: `DatabaseRestoreService.cs`

`DatabaseRestoreService` acts as the primary domain orchestrator:
1. Validates configuration.
2. Pings SQL Server using `IConnectionProvider` to verify credentials.
3. Intercepts `.zip` or `.7z` paths, routing them through `IArchiveExtractionService`.
4. Executes `GetBackupMetadataQuery` to peek into the `.bak` headers.
5. Uses `IDiskSpaceValidator` to ensure the disk won't run out of memory mid-restore based on the metadata.
6. Handles Conflict Resolution (Prompts user via `IConsoleLogger` if DB exists without `-f` bounds).
7. Uses `RestoreDatabaseCommand` using an asynchronous task that streams percentage progress via SQL Server `InfoMessage` event handlers.

---

## Development Setup

### Extending SQL Functionality
If you need to add a new SQL action (e.g., backing up a database before overwriting), create a new class in `Sql/Commands`. 
1. Inject `IConnectionProvider`.
2. Ensure the execution method is declared as `virtual` so it can be natively mocked by Moq in the test suite without necessarily requiring an Interface wrapper for every single command (unless an Interface better serves polymorphism at that time).
3. Register the command as a Singleton in `Program.cs`.

### Running Unit Tests
Tests are located in `MsSqlRestoreTool.Tests`. 
Run the following from the root directory:
```shell
dotnet test mssql-db-restore.sln
```

We utilize `Moq` for strict abstractions. For instance, when testing the `ExecuteRestoreAsync` logic, `IFileSystemService`, `CheckDatabaseExistsQuery` and `RestoreDatabaseCommand` are completely mocked to isolate the orchestration assertions.

### Building
To build a standalone, single-file lightweight executable:
```shell
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
```
