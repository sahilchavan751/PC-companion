namespace OmniControl.Core.Models.History;

public enum PrivilegeLevel
{
    StandardUser,
    ElevatedAdmin
}

public class ChangeHistoryRecord
{
    public long Id { get; set; }
    public string OperationName { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string TargetIdentifier { get; set; } = string.Empty;
    public string? PreviousValue { get; set; }
    public string? NewValue { get; set; }
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public PrivilegeLevel Privilege { get; set; } = PrivilegeLevel.StandardUser;
    public bool IsReversible { get; set; }
    public string? ReversalData { get; set; }
}

public class BackupSnapshot
{
    public long Id { get; set; }
    public string SnapshotName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string Category { get; set; } = string.Empty; // Environment, Startup, Services
    public string PayloadJson { get; set; } = string.Empty;
}
