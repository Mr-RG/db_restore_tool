using System;
using System.IO;

namespace db_restore_tool
{
    public interface IDiskSpaceValidator
    {
        bool HasEnoughSpace(string dataLocation, long requiredSpaceBytes, out long availableSpaceBytes);
        string FormatSize(long bytes);
    }

    public class DiskSpaceValidator : IDiskSpaceValidator
    {
        public bool HasEnoughSpace(string dataLocation, long requiredSpaceBytes, out long availableSpaceBytes)
        {
            availableSpaceBytes = 0;
            
            if (string.IsNullOrWhiteSpace(dataLocation))
                throw new ArgumentException("Data location cannot be null or empty.", nameof(dataLocation));

            string rootPath = Path.GetPathRoot(dataLocation);
            if (string.IsNullOrEmpty(rootPath))
                throw new Exception($"Could not determine root drive for path: {dataLocation}");

            var driveInfo = new DriveInfo(rootPath);
            if (!driveInfo.IsReady)
                throw new Exception($"Drive {rootPath} is not ready.");

            availableSpaceBytes = driveInfo.AvailableFreeSpace;
            return availableSpaceBytes >= requiredSpaceBytes;
        }

        public string FormatSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }

            return $"{len:0.##} {sizes[order]}";
        }
    }
}
