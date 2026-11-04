using OmniControl.Core.Results;

namespace OmniControl.Core.AdminContract;

public enum AdminOperationType
{
    SetMachineEnvironmentVariable,
    DeleteMachineEnvironmentVariable,
    SetMachinePathItems,
    ChangeServiceStartupType,
    ControlService,
    SetMachineStartupEntryState,
    ApplyPowerPlan,
    TerminateElevatedProcess
}

public class AdminOperationRequest
{
    public string RequestId { get; set; } = Guid.NewGuid().ToString();
    public AdminOperationType OperationType { get; set; }
    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class AdminOperationResponse
{
    public string RequestId { get; set; } = string.Empty;
    public bool IsSuccess { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ErrorDetails { get; set; }
    public int ErrorCode { get; set; }

    public static AdminOperationResponse Ok(string requestId, string message) => new()
    {
        RequestId = requestId,
        IsSuccess = true,
        Message = message
    };

    public static AdminOperationResponse Fail(string requestId, string message, string? details = null, int errorCode = 0) => new()
    {
        RequestId = requestId,
        IsSuccess = false,
        Message = message,
        ErrorDetails = details,
        ErrorCode = errorCode
    };

    public OperationResult ToOperationResult() => IsSuccess
        ? OperationResult.Ok(Message)
        : OperationResult.Fail(Message, ErrorDetails, ErrorCode);
}

public interface IAdminElevationService
{
    bool IsAppRunningElevated { get; }
    Task<OperationResult> ExecuteElevatedAsync(AdminOperationRequest request, CancellationToken cancellationToken = default);
    Task<bool> EnsureHelperElevatedAsync(CancellationToken cancellationToken = default);
}
