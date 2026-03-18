# Comprehensive Guide to Unit Testing in .NET

You asked: *"How can I test and other necessary things? I don't know anything about it."*

This guide explains **Unit Testing** in a simple, beginner-friendly way, specifically using the pattern we just implemented in `MsSqlRestoreTool.Tests`.

---

## What is a Unit Test?
Imagine writing a function that adds two numbers together `Add(int a, int b)`. A **Unit Test** is a tiny piece of code that automatically checks if `Add(2, 2)` equals `4`. If it equals `5`, the test "fails", and you know you broke something!

Unit tests are critical for verifying that your code acts the way you expect *before* you run the actual application.

## The Challenge: Physical Systems
Your `RestoreService` was difficult to test originally because it directly relied on:
- 1. **SQL Server Connections** (Connecting to "master" and executing `RESTORE DATABASE`)
- 2. **Windows File System** (`File.Exists`, `.bak` and `.zip` existence)
- 3. **Command Prompt** (Executing `7z.exe` directly via `Process.Start`)

If we wrote a unit test for this, every time you clicked "Run Tests", the test would try to connect to a real SQL server, delete a real database, and extract a real zip file! This makes tests slow, fragile, and dangerous.

## The Solution: Dependency Injection (DI) and "Mocking"
To solve this, we used **Dependency Injection**. We extracted all physical actions into **Interfaces** (`ISqlHelper`, `IFileSystem`, `IProcessRunner`). 

Instead of saying *"Delete this file right now"*, `RestoreService` now says *"Hey `IFileSystem`, delete this file for me."*

### What is "Moq"?
`Moq` (pronounced "Mock") is a library we added to your test project. It creates **fake versions** of your interfaces. 
During a test, we can tell the *Fake FileSystem*: "If anyone asks if a file exists, just lie and say YES without actually checking the hard drive."

## The "Arrange, Act, Assert" Pattern
Every unit test uses this 3-step format. Here is how one of your actual tests (`PerformRestore_DatabaseExists_ForceReplaceTrue_DropsExistingAndRestores`) works:

### 1. Arrange (Set up the scenario)
We configure our Fake systems using `Moq`. Notice how we dictate what the fake systems will return:

```csharp
// "Arrange": Setting up the fake scenario where a Database already exists
string targetDb = "targetdb";

// FAKE: Tell the system the .bak file exists on disk
_mockFileSystem.Setup(fs => fs.FileExists(inputFile)).Returns(true);

// FAKE: Tell the system SQL Server is online
_mockSqlHelper.Setup(sh => sh.DatabaseExists("master")).Returns(true);

// THE CRITICAL FAKE: Tell the system the Target Database ALREADY EXISTS!
_mockSqlHelper.Setup(sh => sh.DatabaseExists(targetDb)).Returns(true);
```

### 2. Act (Execute the real code)
We execute the real `PerformRestore` method, passing in the `-f` (force replace) flag.

```csharp
_service.PerformRestore(inputFile, targetDb, isExplicitName: true, forceReplace: true);
```

### 3. Assert (Verify the outcome)
We check our `Moq` systems to prove that the real code called the methods we expected. Because we used `-f`, the target Database should have been dropped before restoring!

```csharp
// Verify that the system ATTEMPTED to kill connections 1 time
_mockSqlHelper.Verify(sh => sh.KillConnections(targetDb), Times.Once);

// Verify that the system ATTEMPTED to drop the database 1 time
_mockSqlHelper.Verify(sh => sh.DropDatabase(targetDb), Times.Once);

// Verify that the system ATTEMPTED to run RESTORE DATABASE 1 time
_mockSqlHelper.Verify(sh => sh.RestoreDatabase(targetDb, inputFile, It.IsAny<Action<string>>()), Times.Once);
```

### How to Run Tests
1. In Visual Studio, go to **Test > Test Explorer** and click the green "Run All" button.
2. In the terminal, navigate to your root folder and type `dotnet test`.

The system will execute every scenario in milliseconds without touching your hard drive or SQL server!
