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
        Task<string> ExtractArchiveAsync(string archivePath, Action<int> progressCallback, CancellationToken cancellationToken);
        void CleanupTempDirectory(string tempDirectory);
    }

    public class ArchiveExtractionService : IArchiveExtractionService
    {
        private readonly RestoreConfig _config;
        private readonly IFileSystemService _fileSystem;
        private readonly ISevenZipBootstrapper _sevenZipBootstrapper;
        public ArchiveExtractionService(
            RestoreConfig config, 
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

        public async Task<string> ExtractArchiveAsync(string archivePath, Action<int> progressCallback, CancellationToken cancellationToken)
        {
            string sevenZipExe = _sevenZipBootstrapper.EnsureBinariesExist();
            string extractPath = Path.Combine(_config.TempDirectory);
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

            return FindBakFile(extractPath);
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

        private string FindBakFile(string extractPath)
        {
            var bakFiles = _fileSystem.GetFiles(extractPath, "*.bak", SearchOption.TopDirectoryOnly);
            var recentBakPath = bakFiles.OrderByDescending(f => _fileSystem.GetFileCreationTime(f)).FirstOrDefault();

            if (recentBakPath == null) throw new FileNotFoundException("No .bak file found after extraction.");

            return recentBakPath;
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
