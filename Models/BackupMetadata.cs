namespace db_restore_tool.Models
{
    public class BackupMetadata
    {
        public string DatabaseName { get; set; }
        public long RequiredSpaceBytes { get; set; }
        
        // MSSQL Specific
        public string LogicalDataName { get; set; }
        public string LogicalLogName { get; set; }
        public bool IsValid => !string.IsNullOrEmpty(LogicalDataName) && !string.IsNullOrEmpty(LogicalLogName);
    }
}
