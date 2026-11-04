using System.Diagnostics;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using Microsoft.Win32;
using OmniControl.Core.AdminContract;

namespace OmniControl.AdminHelper.Operations;

public static class OperationExecutor
{
    public static AdminOperationResponse Execute(AdminOperationRequest request)
    {
        try
        {
            return request.OperationType switch
            {
                AdminOperationType.SetMachineEnvironmentVariable => ExecuteSetEnv(request),
                AdminOperationType.DeleteMachineEnvironmentVariable => ExecuteDeleteEnv(request),
                AdminOperationType.SetMachinePathItems => ExecuteSetPath(request),
                AdminOperationType.ChangeServiceStartupType => ExecuteChangeServiceStartup(request),
                AdminOperationType.ControlService => ExecuteControlService(request),
                AdminOperationType.SetMachineStartupEntryState => ExecuteStartupEntryState(request),
                AdminOperationType.ApplyPowerPlan => ExecuteApplyPowerPlan(request),
                AdminOperationType.TerminateElevatedProcess => ExecuteTerminateProcess(request),
                _ => AdminOperationResponse.Fail(request.RequestId, $"Unsupported operation: {request.OperationType}")
            };
        }
        catch (Exception ex)
        {
            return AdminOperationResponse.Fail(request.RequestId, ex.Message, ex.ToString());
        }
    }

    private static AdminOperationResponse ExecuteSetEnv(AdminOperationRequest req)
    {
        var name = req.Parameters["Name"];
        var val = req.Parameters["Value"];

        Environment.SetEnvironmentVariable(name, val, EnvironmentVariableTarget.Machine);
        BroadcastEnvironmentChange();

        return AdminOperationResponse.Ok(req.RequestId, $"System environment variable '{name}' was successfully set.");
    }

    private static AdminOperationResponse ExecuteDeleteEnv(AdminOperationRequest req)
    {
        var name = req.Parameters["Name"];

        Environment.SetEnvironmentVariable(name, null, EnvironmentVariableTarget.Machine);
        BroadcastEnvironmentChange();

        return AdminOperationResponse.Ok(req.RequestId, $"System environment variable '{name}' was successfully removed.");
    }

    private static AdminOperationResponse ExecuteSetPath(AdminOperationRequest req)
    {
        var pathValue = req.Parameters["PathValue"];

        Environment.SetEnvironmentVariable("PATH", pathValue, EnvironmentVariableTarget.Machine);
        BroadcastEnvironmentChange();

        return AdminOperationResponse.Ok(req.RequestId, "System PATH was successfully updated.");
    }

    private static AdminOperationResponse ExecuteChangeServiceStartup(AdminOperationRequest req)
    {
        var serviceName = req.Parameters["ServiceName"];
        var modeStr = req.Parameters["StartMode"];

        var startMode = modeStr.ToLowerInvariant() switch
        {
            "automatic" => "auto",
            "manual" => "demand",
            "disabled" => "disabled",
            _ => throw new ArgumentException($"Invalid service start mode: {modeStr}")
        };

        // Use sc.exe config with proper quoting
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = $"config \"{serviceName}\" start= {startMode}",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true
        });

        process?.WaitForExit(5000);

        if (process?.ExitCode == 0)
        {
            return AdminOperationResponse.Ok(req.RequestId, $"Service '{serviceName}' startup mode set to {modeStr}.");
        }

        var err = process?.StandardOutput.ReadToEnd();
        return AdminOperationResponse.Fail(req.RequestId, $"Failed to change service startup type: {err}");
    }

    private static AdminOperationResponse ExecuteControlService(AdminOperationRequest req)
    {
        var serviceName = req.Parameters["ServiceName"];
        var action = req.Parameters["Action"];

        using var sc = new ServiceController(serviceName);

        switch (action.ToLowerInvariant())
        {
            case "start":
                if (sc.Status != ServiceControllerStatus.Running && sc.Status != ServiceControllerStatus.StartPending)
                {
                    sc.Start();
                    sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                }
                return AdminOperationResponse.Ok(req.RequestId, $"Service '{serviceName}' started.");

            case "stop":
                if (sc.Status != ServiceControllerStatus.Stopped && sc.Status != ServiceControllerStatus.StopPending)
                {
                    sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                }
                return AdminOperationResponse.Ok(req.RequestId, $"Service '{serviceName}' stopped.");

            case "restart":
                if (sc.Status == ServiceControllerStatus.Running)
                {
                    sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                }
                sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                return AdminOperationResponse.Ok(req.RequestId, $"Service '{serviceName}' restarted.");

            default:
                return AdminOperationResponse.Fail(req.RequestId, $"Unknown action: {action}");
        }
    }

    private static AdminOperationResponse ExecuteStartupEntryState(AdminOperationRequest req)
    {
        var subKey = req.Parameters["SubKey"];
        var valueName = req.Parameters["ValueName"];
        var isEnabled = bool.Parse(req.Parameters["IsEnabled"]);

        const string approvedSubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

        using var approvedKey = Registry.LocalMachine.CreateSubKey(approvedSubKey, writable: true);
        if (approvedKey != null)
        {
            // Windows StartupApproved binary flag: 0x02 = enabled, 0x03 = disabled (or 12 bytes blob)
            var bytes = isEnabled ? new byte[] { 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 } : new byte[] { 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            approvedKey.SetValue(valueName, bytes, RegistryValueKind.Binary);
            return AdminOperationResponse.Ok(req.RequestId, $"Machine startup entry '{valueName}' is now {(isEnabled ? "enabled" : "disabled")}.");
        }

        return AdminOperationResponse.Fail(req.RequestId, "Failed to open machine StartupApproved registry key.");
    }

    private static AdminOperationResponse ExecuteApplyPowerPlan(AdminOperationRequest req)
    {
        var planGuid = Guid.Parse(req.Parameters["PlanGuid"]);
        var res = PowerSetActiveScheme(IntPtr.Zero, ref planGuid);

        if (res == 0)
        {
            return AdminOperationResponse.Ok(req.RequestId, $"Power plan {planGuid} applied successfully.");
        }

        return AdminOperationResponse.Fail(req.RequestId, $"PowerSetActiveScheme returned error code {res}");
    }

    private static AdminOperationResponse ExecuteTerminateProcess(AdminOperationRequest req)
    {
        var pid = int.Parse(req.Parameters["ProcessId"]);
        var process = Process.GetProcessById(pid);
        process.Kill(entireProcessTree: true);

        return AdminOperationResponse.Ok(req.RequestId, $"Process {pid} ({process.ProcessName}) was terminated.");
    }

    private static void BroadcastEnvironmentChange()
    {
        try
        {
            SendMessageTimeout((IntPtr)0xffff, 0x001A, UIntPtr.Zero, "Environment", 2, 2000, out _);
        }
        catch
        {
            // Non-critical if broadcast timeout expires
        }
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, UIntPtr wParam, string lParam, uint fuFlags, uint uTimeout, out UIntPtr lpdwResult);

    [DllImport("Powrprof.dll", SetLastError = true)]
    private static extern uint PowerSetActiveScheme(IntPtr UserRootPowerKey, ref Guid SchemeGuid);
}
