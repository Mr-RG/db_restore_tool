using System.Collections.Generic;
using System.Text.Json;
using Xunit;

namespace mssql_db_restore.Tests
{
    public class RestoreConfigTests
    {
        [Fact]
        public void Deserialize_WithSingleZipPassword_ShouldDeserializeToList()
        {
            // Arrange
            string json = @"{
                ""ServerName"": ""TestServer"",
                ""ZipPassword"": ""mypassword""
            }";

            // Act
            var config = JsonSerializer.Deserialize<RestoreConfig>(json);

            // Assert
            Assert.NotNull(config.ZipPassword);
            Assert.Single(config.ZipPassword);
            Assert.Equal("mypassword", config.ZipPassword[0]);
            Assert.Equal("TestServer", config.ServerName);
        }

        [Fact]
        public void Deserialize_WithArrayZipPassword_ShouldDeserializeToList()
        {
            // Arrange
            string json = @"{
                ""ServerName"": ""TestServer"",
                ""ZipPassword"": [""pass1"", ""pass2""]
            }";

            // Act
            var config = JsonSerializer.Deserialize<RestoreConfig>(json);

            // Assert
            Assert.NotNull(config.ZipPassword);
            Assert.Equal(2, config.ZipPassword.Count);
            Assert.Equal("pass1", config.ZipPassword[0]);
            Assert.Equal("pass2", config.ZipPassword[1]);
        }
    }
}
