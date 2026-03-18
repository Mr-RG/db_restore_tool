using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using mssql_db_restore;
using mssql_db_restore.Sql;
using mssql_db_restore.Sql.Queries;
using mssql_db_restore.Sql.Commands;

namespace mssql_db_restore.Tests
{
    public class RestoreServiceTests
    {
        private readonly Mock<IConnectionProvider> _mockConnectionProvider;
        private readonly Mock<CheckDatabaseExistsQuery> _mockCheckExist;
        private readonly Mock<GetDatabaseSizeQuery> _mockGetSize;
        private readonly Mock<GetBackupMetadataQuery> _mockGetMetadata;
        private readonly Mock<KillConnectionsCommand> _mockKillConnections;
        private readonly Mock<DropDatabaseCommand> _mockDropDatabase;
        private readonly Mock<RestoreDatabaseCommand> _mockRestoreDatabase;
        
        private readonly Mock<IConsoleLogger> _mockLogger;
        private readonly Mock<IDiskSpaceValidator> _mockDiskSpace;
        private readonly Mock<IArchiveExtractionService> _mockExtractionService;
        private readonly Mock<IFileSystemService> _mockFileSystem;
        
        private readonly RestoreConfig _config;
        private readonly DatabaseRestoreService _service;

        public RestoreServiceTests()
        {
            _mockConnectionProvider = new Mock<IConnectionProvider>();
            
            // Pass null to constructors because Moq just bypasses constructor logic when mocking classes
            _mockCheckExist = new Mock<CheckDatabaseExistsQuery>(null);
            _mockGetSize = new Mock<GetDatabaseSizeQuery>(null);
            _mockGetMetadata = new Mock<GetBackupMetadataQuery>(null);
            _mockKillConnections = new Mock<KillConnectionsCommand>(null);
            _mockDropDatabase = new Mock<DropDatabaseCommand>(null);
            _mockRestoreDatabase = new Mock<RestoreDatabaseCommand>(null, null);

            _mockLogger = new Mock<IConsoleLogger>();
            _mockDiskSpace = new Mock<IDiskSpaceValidator>();
            _mockExtractionService = new Mock<IArchiveExtractionService>();
            _mockFileSystem = new Mock<IFileSystemService>();

            _config = new RestoreConfig
            {
                ServerName = "TestServer",
                Username = "sa",
                Password = "pwd",
                TempDirectory = @"C:\Temp",
                DataLocation = @"C:\Data\"
            };

            _service = new DatabaseRestoreService(
                _config,
                _mockConnectionProvider.Object,
                _mockCheckExist.Object,
                _mockGetSize.Object,
                _mockGetMetadata.Object,
                _mockKillConnections.Object,
                _mockDropDatabase.Object,
                _mockRestoreDatabase.Object,
                _mockLogger.Object,
                _mockDiskSpace.Object,
                _mockExtractionService.Object,
                _mockFileSystem.Object
            );
        }

        [Fact]
        public async Task ExecuteRestoreAsync_DirectBakFile_Success()
        {
            // Arrange
            string inputPath = @"C:\backups\testdb.bak";
            var context = new RestoreContext(inputPath, "targetdb", forceReplace: true);
            
            _mockFileSystem.Setup(fs => fs.FileExists(It.IsAny<string>())).Returns(true);
            _mockFileSystem.Setup(fs => fs.GetFileSize(It.IsAny<string>())).Returns(1000);
            
            _mockExtractionService.Setup(e => e.IsArchive(inputPath)).Returns(false);

            _mockGetMetadata.Setup(m => m.ExecuteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BackupMetadata { RequiredSpaceBytes = 500, DatabaseName = "targetdb", LogicalDataName = "data", LogicalLogName = "log" });

            long outSpace = 10000;
            _mockDiskSpace.Setup(d => d.HasEnoughSpace(It.IsAny<string>(), It.IsAny<long>(), out outSpace)).Returns(true);

            // Mock database already existing to test Drop path
            _mockCheckExist.Setup(c => c.ExecuteAsync("targetdb", It.IsAny<CancellationToken>())).ReturnsAsync(true);
            
            _mockGetSize.Setup(s => s.ExecuteAsync("targetdb", It.IsAny<CancellationToken>())).ReturnsAsync(100.0);

            // Act
            await _service.ExecuteRestoreAsync(context, CancellationToken.None);

            // Assert
            _mockConnectionProvider.Verify(c => c.EnsureConnectionAsync(It.IsAny<CancellationToken>()), Times.Once);
            _mockKillConnections.Verify(c => c.ExecuteAsync("targetdb", It.IsAny<CancellationToken>()), Times.Once);
            _mockDropDatabase.Verify(c => c.ExecuteAsync("targetdb", It.IsAny<CancellationToken>()), Times.Once);
            _mockRestoreDatabase.Verify(c => c.ExecuteAsync("targetdb", inputPath, It.IsAny<BackupMetadata>(), It.IsAny<Action<string>>(), It.IsAny<CancellationToken>()), Times.Once);
            _mockLogger.Verify(l => l.LogSummary(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }
    }
}
