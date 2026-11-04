using System.ServiceProcess;
using Microsoft.Win32;
using OmniControl.Core.AdminContract;
using OmniControl.Core.Interfaces;
using OmniControl.Core.Models.History;
using OmniControl.Core.Models.Services;
using OmniControl.Core.Results;

namespace OmniControl.Infrastructure.Services;

public class WindowsServicesService : IWindowsServicesService
{
    private static readonly HashSet<string> EssentialServiceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "RpcSs", "PlugPlay", "EventLog", "DcomLaunch", "SamSs", "Winmgmt", "CryptSvc", "gpsvc", "BrokerInfrastructure", "LSM"
    };

    private readonly IAdminElevationService _elevationService;
    private readonly IOperationHistoryService _historyService;

    public WindowsServicesService(IAdminElevationService elevationService, IOperationHistoryService historyService)
    {
        _elevationService = elevationService;
        _historyService = historyService;
    }

    public async Task<List<WindowsServiceItem>> GetServicesAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var list = new List<WindowsServiceItem>();
            var services = ServiceController.GetServices();

            foreach (var sc in services)
            {
                if (cancellationToken.IsCancellationRequested) break;

                try
                {
                    var item = new WindowsServiceItem
                    {
                        ServiceName = sc.ServiceName,
                        DisplayName = sc.DisplayName,
                        State = MapState(sc.Status),
                        CanStop = sc.CanStop,
                        CanPauseAndContinue = sc.CanPauseAndContinue,
                        CanShutdown = sc.CanShutdown,
                        IsEssentialService = EssentialServiceNames.Contains(sc.ServiceName)
                    };

                    // Registry query for start mode, description, image path
                    try
                    {
                        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{sc.ServiceName}");
                        if (key != null)
                        {
                            item.Description = key.GetValue("Description")?.ToString() ?? string.Empty;
                            item.BinaryPath = key.GetValue("ImagePath")?.ToString() ?? string.Empty;
                            item.AccountName = key.GetValue("ObjectName")?.ToString() ?? string.Empty;

                            var startVal = key.GetValue("Start");
                            if (startVal is int startInt)
                            {
                                var delayed = key.GetValue("DelayedAutostart") as int? == 1;
                                item.StartMode = startInt switch
                                {
                                    2 => delayed ? WindowsServiceStartMode.AutomaticDelayedStart : WindowsServiceStartMode.Automatic,
                                    3 => WindowsServiceStartMode.Manual,
                                    4 => WindowsServiceStartMode.Disabled,
                                    _ => WindowsServiceStartMode.Unknown
                                };
                            }
                        }
                    }
                    catch
                    {
                        // Restricted service keys
                    }

                    list.Add(item);
                }
                catch
                {
                    // Service query issue
                }
                finally
                {
                    sc.Dispose();
                }
            }

            return list.OrderBy(x => x.DisplayName).ToList();
        }, cancellationToken);
    }

    public async Task<OperationResult> StartServiceAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        return await ControlServiceInternalAsync(serviceName, "Start", cancellationToken);
    }

    public async Task<OperationResult> StopServiceAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        if (EssentialServiceNames.Contains(serviceName))
        {
            return OperationResult.Fail($"Stopping critical system service '{serviceName}' is disabled to prevent Windows instability.");
        }

        return await ControlServiceInternalAsync(serviceName, "Stop", cancellationToken);
    }

    public async Task<OperationResult> RestartServiceAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        return await ControlServiceInternalAsync(serviceName, "Restart", cancellationToken);
    }

    public async Task<OperationResult> ChangeStartupTypeAsync(string serviceName, WindowsServiceStartMode startMode, CancellationToken cancellationToken = default)
    {
        var services = await GetServicesAsync(cancellationToken);
        var service = services.FirstOrDefault(s => s.ServiceName.Equals(serviceName, StringComparison.OrdinalIgnoreCase));
        var prevMode = service?.StartMode.ToString();

        var result = await _elevationService.ExecuteElevatedAsync(new AdminOperationRequest
        {
            OperationType = AdminOperationType.ChangeServiceStartupType,
            Parameters =
            {
                ["ServiceName"] = serviceName,
                ["StartMode"] = startMode.ToString()
            }
        }, cancellationToken);

        if (result.IsSuccess)
        {
            await _historyService.RecordOperationAsync(new ChangeHistoryRecord
            {
                OperationName = "ChangeServiceStartupType",
                ModuleName = "Services",
                TargetIdentifier = serviceName,
                PreviousValue = prevMode,
                NewValue = startMode.ToString(),
                IsSuccess = true,
                IsReversible = true,
                Privilege = PrivilegeLevel.ElevatedAdmin
            });
        }

        return result;
    }

    private async Task<OperationResult> ControlServiceInternalAsync(string serviceName, string action, CancellationToken cancellationToken)
    {
        // First try standard user execution if permitted
        try
        {
            using var sc = new ServiceController(serviceName);
            if (action == "Start")
            {
                sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(5));
                return OperationResult.Ok($"Service '{serviceName}' started.");
            }
            if (action == "Stop")
            {
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(5));
                return OperationResult.Ok($"Service '{serviceName}' stopped.");
            }
        }
        catch (System.InvalidOperationException)
        {
            // Requires elevation: fallback to elevated helper
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Access denied: fallback to elevated helper
        }

        var result = await _elevationService.ExecuteElevatedAsync(new AdminOperationRequest
        {
            OperationType = AdminOperationType.ControlService,
            Parameters =
            {
                ["ServiceName"] = serviceName,
                ["Action"] = action
            }
        }, cancellationToken);

        await _historyService.RecordOperationAsync(new ChangeHistoryRecord
        {
            OperationName = $"{action}Service",
            ModuleName = "Services",
            TargetIdentifier = serviceName,
            NewValue = action,
            IsSuccess = result.IsSuccess,
            Privilege = PrivilegeLevel.ElevatedAdmin
        });

        return result;
    }

    private static WindowsServiceState MapState(ServiceControllerStatus status) => status switch
    {
        ServiceControllerStatus.Running => WindowsServiceState.Running,
        ServiceControllerStatus.Stopped => WindowsServiceState.Stopped,
        ServiceControllerStatus.Paused => WindowsServiceState.Paused,
        ServiceControllerStatus.StartPending => WindowsServiceState.StartPending,
        ServiceControllerStatus.StopPending => WindowsServiceState.StopPending,
        ServiceControllerStatus.ContinuePending => WindowsServiceState.ContinuePending,
        ServiceControllerStatus.PausePending => WindowsServiceState.PausePending,
        _ => WindowsServiceState.Unknown
    };
}
