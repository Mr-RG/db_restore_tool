using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace db_restore_tool
{
    public interface IArchiveExtractionService
    {
        bool IsArchive(string path);
        Task<string> ExtractArchiveAsync(string archivePath, string extractPath, string serverType, Action<int> progressCallback, CancellationToken cancellationToken);
        void CleanupTempDirectory(string tempDirectory);
    }

    public class ArchiveExtractionService : IArchiveExtractionService
    {
        private readonly db_restore_tool.Models.AppConfig _config;
        private readonly IFileSystemService _fileSystem;
        private readonly ISevenZipBootstrapper _sevenZipBootstrapper;
        public ArchiveExtractionService(
            db_restore_tool.Models.AppConfig config, 
            IFileSystemService fileSystem, 
            ISevenZipBootstrapper sevenZipBootstrapper)
        {
            _config = config;
            _fileSystem = fileSystem;
            _sevenZipBootstrapper = sevenZipBootstrapper;
        }

        public bool IsArchive(string path)
        {
            var ext = Path.GetExtension(path)?.ToLower();
            return ext == ".7z" || ext == ".zip" || ext == ".rar";
        }

        public async Task<string> ExtractArchiveAsync(string archivePath, string extractPath, string serverType, Action<int> progressCallback, CancellationToken cancellationToken)
        {
            string sevenZipExe = _sevenZipBootstrapper.EnsureBinariesExist();
            _fileSystem.CreateDirectory(extractPath);

            var passwords = _config.ZipPassword ?? new List<string>();
            if (passwords.Count == 0) passwords.Add("");

            bool extractionSuccess = false;
            foreach (var pwd in passwords)
            {
                if (await TryExtractWithPasswordAsync(sevenZipExe, archivePath, extractPath, pwd, progressCallback, cancellationToken))
                {
                    extractionSuccess = true;
                    break;
                }
            }

            if (!extractionSuccess)
            {
                try
                {
                    _fileSystem.DeleteDirectory(extractPath, true);
                }
                catch { }

                throw new Exception("7-Zip extraction failed with all provided passwords.");
            }

            return FindRestoreFile(extractPath, serverType);
        }

        public void CleanupTempDirectory(string tempDirectory)
        {
            if (string.IsNullOrWhiteSpace(tempDirectory)) return;
            
            try
            {
                if (_fileSystem.DirectoryExists(tempDirectory) && tempDirectory.StartsWith(_config.TempDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    _fileSystem.DeleteDirectory(tempDirectory, true);
                }
            }
            catch
            {
                // Ignored
            }
        }

        private string FindRestoreFile(string extractPath, string serverType)
        {
            string[] extensions = serverType.ToUpperInvariant() switch
            {
                "MSSQL" => new[] { "*.bak" },
                "POSTGRESQL" => new[] { "*.sql", "*.dump", "*.backup" },
                _ => new[] { "*.bak", "*.sql", "*.dump" }
            };

            foreach (var ext in extensions)
            {
                var files = _fileSystem.GetFiles(extractPath, ext, SearchOption.TopDirectoryOnly);
                if (files.Any())
                    return files.OrderByDescending(f => _fileSystem.GetFileCreationTime(f)).First();
            }

            throw new FileNotFoundException($"No valid restore file found after extraction for server type '{serverType}'.");
        }

        private async Task<bool> TryExtractWithPasswordAsync(string sevenZipExe, string archivePath, string extractPath, string password, Action<int> progressCallback, CancellationToken cancellationToken = default)
        {
            string args = $"x \"{archivePath}\" -o\"{extractPath}\" -y -bsp1 -mmt=on";
            if (!string.IsNullOrEmpty(password)) args += $" -p\"{password}\"";

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = sevenZipExe,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            bool success = false;
            int lastEmittedPercent = -1;

            using (var process = System.Diagnostics.Process.Start(psi))
            {
                process.OutputDataReceived += (s, e) =>
                {
                    if (e.Data != null && e.Data.Contains("%"))
                    {
                        var parts = e.Data.Trim().Split('%');
                        if (parts.Length > 0 && int.TryParse(parts[0].Trim(), out int percent))
                        {
                            if (percent != lastEmittedPercent)
                            {
                                int displayPercent = Math.Min(99, percent);
                                if (displayPercent != lastEmittedPercent) 
                                {
                                    lastEmittedPercent = displayPercent;
                                    progressCallback?.Invoke(displayPercent);
                                }
                            }
                        }
                    }
                };
                process.BeginOutputReadLine();
                
                await process.WaitForExitAsync(cancellationToken);
                
                if (process.ExitCode == 0)
                {
                    success = true;
                    if (lastEmittedPercent != 100)
                    {
                        progressCallback?.Invoke(100);
                        lastEmittedPercent = 100;
                    }
                }
            }

            return success;
        }
    }
}
