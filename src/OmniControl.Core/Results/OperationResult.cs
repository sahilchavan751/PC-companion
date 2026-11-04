namespace OmniControl.Core.Results;

public enum OperationStatus
{
    Success,
    PartialSuccess,
    Unsupported,
    RequiresElevation,
    Failed,
    Cancelled
}

public class OperationResult
{
    public bool IsSuccess => Status == OperationStatus.Success;
    public OperationStatus Status { get; set; } = OperationStatus.Success;
    public string Message { get; set; } = string.Empty;
    public string? ErrorDetails { get; set; }
    public int ErrorCode { get; set; }

    public static OperationResult Ok(string message = "Operation completed successfully.")
        => new() { Status = OperationStatus.Success, Message = message };

    public static OperationResult Partial(string message, string? details = null)
        => new() { Status = OperationStatus.PartialSuccess, Message = message, ErrorDetails = details };

    public static OperationResult Unsupported(string message = "Operation is not supported on this device/hardware.")
        => new() { Status = OperationStatus.Unsupported, Message = message };

    public static OperationResult RequiresElevated(string message = "Administrator permission is required for this action.")
        => new() { Status = OperationStatus.RequiresElevation, Message = message };

    public static OperationResult Fail(string message, string? details = null, int errorCode = 0)
        => new() { Status = OperationStatus.Failed, Message = message, ErrorDetails = details, ErrorCode = errorCode };

    public static OperationResult Cancel(string message = "Operation was cancelled.")
        => new() { Status = OperationStatus.Cancelled, Message = message };
}

public class OperationResult<T> : OperationResult
{
    public T? Data { get; set; }

    public static OperationResult<T> Ok(T data, string message = "Success")
        => new() { Status = OperationStatus.Success, Message = message, Data = data };

    public static new OperationResult<T> Partial(string message, string? details = null)
        => new() { Status = OperationStatus.PartialSuccess, Message = message, ErrorDetails = details };

    public static new OperationResult<T> Unsupported(string message = "Not supported")
        => new() { Status = OperationStatus.Unsupported, Message = message };

    public static new OperationResult<T> RequiresElevated(string message = "Requires elevation")
        => new() { Status = OperationStatus.RequiresElevation, Message = message };

    public static new OperationResult<T> Fail(string message, string? details = null, int errorCode = 0)
        => new() { Status = OperationStatus.Failed, Message = message, ErrorDetails = details, ErrorCode = errorCode };

    public static new OperationResult<T> Cancel(string message = "Cancelled")
        => new() { Status = OperationStatus.Cancelled, Message = message };
}
