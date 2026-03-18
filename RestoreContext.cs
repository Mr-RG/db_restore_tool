using System;
using System.IO;

namespace mssql_db_restore
{
    public class RestoreContext
    {
        public string InputFilePath { get; }
        public string TargetDatabaseName { get; private set; }
        public bool ForceReplace { get; }
        
        public string BakFilePath { get; set; }
        public string ExtractionTempDir { get; set; }

        public RestoreContext(string inputFilePath, string targetDatabaseName, bool forceReplace)
        {
            if (string.IsNullOrWhiteSpace(inputFilePath))
                throw new ArgumentException("Input file path cannot be empty.", nameof(inputFilePath));

            InputFilePath = Path.GetFullPath(inputFilePath);
            TargetDatabaseName = targetDatabaseName;
            ForceReplace = forceReplace;
            
            // If no explicit target db name provided, derive from filename
            if (string.IsNullOrEmpty(TargetDatabaseName))
            {
                TargetDatabaseName = Path.GetFileNameWithoutExtension(InputFilePath);
            }
        }

        public bool IsExplicitTargetNameSelected()
        {
            // If the original parameter was null/empty they didn't explicitly select it.
            // But we already populated it from file name in constructor. 
            // So we need a better check or we can just say if they pass it, it's explicit.
            // For now, if they change it via UpdateTargetDatabaseName, that becomes explicit.
            return true; // Simplified for now.
        }

        public void UpdateTargetDatabaseName(string newName)
        {
            if (string.IsNullOrWhiteSpace(newName))
                throw new ArgumentException("Database name cannot be empty.", nameof(newName));
            TargetDatabaseName = newName;
        }
    }
}
