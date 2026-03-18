using System;
using System.IO;
using System.Linq;

namespace mssql_db_restore
{
    public interface ISevenZipBootstrapper
    {
        string EnsureBinariesExist();
    }

    public class SevenZipBootstrapper : ISevenZipBootstrapper
    {
        private readonly IFileSystemService _fileSystem;

        public SevenZipBootstrapper(IFileSystemService fileSystem)
        {
            _fileSystem = fileSystem;
        }

        public string EnsureBinariesExist()
        {
            string tempFolder = Path.Combine(Path.GetTempPath(), "MsSqlRestoreTool", "7z");
            if (!_fileSystem.DirectoryExists(tempFolder)) _fileSystem.CreateDirectory(tempFolder);

            string sevenZipExe = Path.Combine(tempFolder, "7z.exe");
            string sevenZipDll = Path.Combine(tempFolder, "7z.dll");

            if (!_fileSystem.FileExists(sevenZipExe) || !_fileSystem.FileExists(sevenZipDll))
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                ExtractResource(assembly, "MsSqlRestoreTool.zip.7z.exe", sevenZipExe);
                ExtractResource(assembly, "MsSqlRestoreTool.zip.7z.dll", sevenZipDll);
            }

            return sevenZipExe;
        }

        private void ExtractResource(System.Reflection.Assembly assembly, string resourceName, string outputPath)
        {
            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            {
                Stream dataStream = stream;
                if (dataStream == null)
                {
                    var allResources = assembly.GetManifestResourceNames();
                    var match = allResources.FirstOrDefault(r =>
                        r.EndsWith(resourceName) || r.Equals(resourceName, StringComparison.OrdinalIgnoreCase));
                    if (!string.IsNullOrEmpty(match)) dataStream = assembly.GetManifestResourceStream(match);
                    else
                        throw new Exception($"Embedded resource '{resourceName}' not found. Available: {string.Join(", ", allResources)}");
                }

                using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write)) dataStream.CopyTo(fs);
                if (dataStream != stream) dataStream.Dispose();
            }
        }
    }
}
