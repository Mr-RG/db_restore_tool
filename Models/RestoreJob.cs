using System;

namespace db_restore_tool.Models
{
    public enum JobStatus
    {
        Pending,
        Extracting,
        Restoring,
        Completed,
        Failed
    }

    public class RestoreJob
    {
        public string JobId { get; } = Guid.NewGuid().ToString("N").Substring(0, 8);
        public AppConfig Config { get; init; }
        public ServerConfig Server { get; init; }
        public RestoreContext Context { get; init; }
        public string IsolatedTempDir { get; init; }
        public JobStatus Status { get; set; } = JobStatus.Pending;
    }
}
