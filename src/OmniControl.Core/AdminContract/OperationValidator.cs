using System.Text.RegularExpressions;

namespace OmniControl.Core.AdminContract;

public static class OperationValidator
{
    private static readonly Regex ValidIdentifierRegex = new("^[a-zA-Z0-9_.-]{1,128}$", RegexOptions.Compiled);
    private static readonly HashSet<string> AllowedRegistrySubKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run",
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"
    };

    private static readonly HashSet<string> ProtectedServiceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "RpcSs", "PlugPlay", "EventLog", "DcomLaunch", "SamSs", "Winmgmt", "CryptSvc", "gpsvc"
    };

    public static (bool IsValid, string? ErrorMessage) Validate(AdminOperationRequest request)
    {
        if (request == null)
            return (false, "Request is null.");

        return request.OperationType switch
        {
            AdminOperationType.SetMachineEnvironmentVariable => ValidateSetEnv(request),
            AdminOperationType.DeleteMachineEnvironmentVariable => ValidateDeleteEnv(request),
            AdminOperationType.SetMachinePathItems => ValidateSetPath(request),
            AdminOperationType.ChangeServiceStartupType => ValidateChangeService(request),
            AdminOperationType.ControlService => ValidateControlService(request),
            AdminOperationType.SetMachineStartupEntryState => ValidateStartupEntry(request),
            AdminOperationType.ApplyPowerPlan => ValidatePowerPlan(request),
            AdminOperationType.TerminateElevatedProcess => ValidateProcessTermination(request),
            _ => (false, $"Unknown or unsupported operation type: {request.OperationType}")
        };
    }

    private static (bool IsValid, string? ErrorMessage) ValidateSetEnv(AdminOperationRequest req)
    {
        if (!req.Parameters.TryGetValue("Name", out var name) || string.IsNullOrWhiteSpace(name))
            return (false, "Environment variable name is required.");

        if (!ValidIdentifierRegex.IsMatch(name))
            return (false, $"Invalid characters in environment variable name: {name}");

        if (!req.Parameters.TryGetValue("Value", out var _))
            return (false, "Environment variable value parameter is missing.");

        return (true, null);
    }

    private static (bool IsValid, string? ErrorMessage) ValidateDeleteEnv(AdminOperationRequest req)
    {
        if (!req.Parameters.TryGetValue("Name", out var name) || string.IsNullOrWhiteSpace(name))
            return (false, "Environment variable name is required.");

        if (!ValidIdentifierRegex.IsMatch(name))
            return (false, $"Invalid characters in environment variable name: {name}");

        return (true, null);
    }

    private static (bool IsValid, string? ErrorMessage) ValidateSetPath(AdminOperationRequest req)
    {
        if (!req.Parameters.TryGetValue("PathValue", out var pathValue))
            return (false, "PathValue parameter is missing.");

        if (pathValue.Length > 32767)
            return (false, "Path value exceeds maximum allowed system length.");

        return (true, null);
    }

    private static (bool IsValid, string? ErrorMessage) ValidateChangeService(AdminOperationRequest req)
    {
        if (!req.Parameters.TryGetValue("ServiceName", out var serviceName) || string.IsNullOrWhiteSpace(serviceName))
            return (false, "ServiceName is required.");

        if (!ValidIdentifierRegex.IsMatch(serviceName))
            return (false, $"Invalid service name: {serviceName}");

        if (!req.Parameters.TryGetValue("StartMode", out var mode) || string.IsNullOrWhiteSpace(mode))
            return (false, "StartMode is required.");

        return (true, null);
    }

    private static (bool IsValid, string? ErrorMessage) ValidateControlService(AdminOperationRequest req)
    {
        if (!req.Parameters.TryGetValue("ServiceName", out var serviceName) || string.IsNullOrWhiteSpace(serviceName))
            return (false, "ServiceName is required.");

        if (!ValidIdentifierRegex.IsMatch(serviceName))
            return (false, $"Invalid service name: {serviceName}");

        if (!req.Parameters.TryGetValue("Action", out var action) || string.IsNullOrWhiteSpace(action))
            return (false, "Action is required.");

        if (action.Equals("Stop", StringComparison.OrdinalIgnoreCase) && ProtectedServiceNames.Contains(serviceName))
            return (false, $"Stopping core Windows system service '{serviceName}' is blocked for system stability.");

        return (true, null);
    }

    private static (bool IsValid, string? ErrorMessage) ValidateStartupEntry(AdminOperationRequest req)
    {
        if (!req.Parameters.TryGetValue("SubKey", out var subKey) || string.IsNullOrWhiteSpace(subKey))
            return (false, "Registry subkey is required.");

        if (!AllowedRegistrySubKeys.Contains(subKey))
            return (false, $"Unauthorized registry path: {subKey}");

        if (!req.Parameters.TryGetValue("ValueName", out var valueName) || string.IsNullOrWhiteSpace(valueName))
            return (false, "ValueName is required.");

        return (true, null);
    }

    private static (bool IsValid, string? ErrorMessage) ValidatePowerPlan(AdminOperationRequest req)
    {
        if (!req.Parameters.TryGetValue("PlanGuid", out var guidStr) || !Guid.TryParse(guidStr, out _))
            return (false, "A valid PlanGuid is required.");

        return (true, null);
    }

    private static (bool IsValid, string? ErrorMessage) ValidateProcessTermination(AdminOperationRequest req)
    {
        if (!req.Parameters.TryGetValue("ProcessId", out var pidStr) || !int.TryParse(pidStr, out var pid))
            return (false, "A valid ProcessId integer is required.");

        if (pid <= 4)
            return (false, "Cannot terminate critical system kernel processes (PID 0 or 4).");

        return (true, null);
    }
}
