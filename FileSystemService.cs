using System;
using System.IO;

namespace mssql_db_restore
{
    public interface IFileSystemService
    {
        bool FileExists(string path);
        bool DirectoryExists(string path);
        void CreateDirectory(string path);
        void DeleteDirectory(string path, bool recursive);
        string[] GetFiles(string path, string searchPattern, SearchOption searchOption);
        long GetFileSize(string path);
        DateTime GetFileCreationTime(string path);
    }

    public class FileSystemService : IFileSystemService
    {
        public bool FileExists(string path) => File.Exists(path);
        public bool DirectoryExists(string path) => Directory.Exists(path);
        public void CreateDirectory(string path) => Directory.CreateDirectory(path);
        public void DeleteDirectory(string path, bool recursive) => Directory.Delete(path, recursive);
        public string[] GetFiles(string path, string searchPattern, SearchOption searchOption) => Directory.GetFiles(path, searchPattern, searchOption);
        public long GetFileSize(string path) => new FileInfo(path).Length;
        public DateTime GetFileCreationTime(string path) => new FileInfo(path).CreationTime;
    }
}
