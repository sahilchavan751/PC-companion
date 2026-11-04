using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using OmniControl.Core.AdminContract;
using OmniControl.Core.Interfaces;
using OmniControl.Core.Models.Processes;
using OmniControl.Core.Results;
using OmniControl.Infrastructure.WindowsInterop;

namespace OmniControl.Infrastructure.Processes;

public class ProcessManagerService : IProcessManagerService
{
    private static readonly HashSet<string> CriticalProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "smss", "csrss", "wininit", "services", "lsass", "svchost", "fontdrvhost"
    };

    private readonly IAdminElevationService _elevationService;
    private readonly Dictionary<int, (TimeSpan CpuTime, DateTime SampleTime)> _cpuHistory = new();
    private readonly object _cpuLock = new();

    public ProcessManagerService(IAdminElevationService elevationService)
    {
        _elevationService = elevationService;
    }

    public async Task<List<ProcessItem>> GetProcessesAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var list = new List<ProcessItem>();
            var processes = Process.GetProcesses();
            var now = DateTime.UtcNow;

            foreach (var p in processes)
            {
                if (cancellationToken.IsCancellationRequested) break;

                try
                {
                    var item = new ProcessItem
                    {
                        Id = p.Id,
                        Name = p.ProcessName,
                        WorkingSetBytes = p.WorkingSet64,
                        IsCritical = CriticalProcessNames.Contains(p.ProcessName) || p.Id <= 4
                    };

                    try
                    {
                        item.PrivateMemoryBytes = p.PrivateMemorySize64;
                        item.ThreadCount = p.Threads.Count;
                        item.HandleCount = p.HandleCount;
                        item.IsResponding = p.Responding;
                        item.Priority = p.PriorityClass;
                        item.StartTime = p.StartTime;
                        item.TotalProcessorTime = p.TotalProcessorTime;
                    }
                    catch
                    {
                        // Some system processes restrict thread/handle inspection
                    }

                    item.ExecutablePath = TryGetProcessPath(p);

                    if (!string.IsNullOrEmpty(item.ExecutablePath))
                    {
                        try
                        {
                            if (File.Exists(item.ExecutablePath))
                            {
                                var versionInfo = FileVersionInfo.GetVersionInfo(item.ExecutablePath);
                                item.Description = versionInfo.FileDescription ?? string.Empty;
                                item.Publisher = versionInfo.CompanyName ?? string.Empty;
                            }
                        }
                        catch { }

                        item.IconPath = AppIconExtractor.GetOrExtractIcon(item.ExecutablePath);
                    }

                    // CPU % calculation
                    if (item.TotalProcessorTime.HasValue)
                    {
                        lock (_cpuLock)
                        {
                            if (_cpuHistory.TryGetValue(p.Id, out var prev))
                            {
                                var elapsed = (now - prev.SampleTime).TotalMilliseconds;
                                var cpuDelta = (item.TotalProcessorTime.Value - prev.CpuTime).TotalMilliseconds;

                                if (elapsed > 200)
                                {
                                    var percent = (cpuDelta / (elapsed * global::System.Environment.ProcessorCount)) * 100.0;
                                    item.CpuPercent = Math.Max(0.0, Math.Min(100.0, Math.Round(percent, 1)));
                                    _cpuHistory[p.Id] = (item.TotalProcessorTime.Value, now);
                                }
                            }
                            else
                            {
                                _cpuHistory[p.Id] = (item.TotalProcessorTime.Value, now);
                            }
                        }
                    }

                    // Categorize
                    if (item.IsCritical || item.ExecutablePath.Contains(@"\Windows\", StringComparison.OrdinalIgnoreCase))
                    {
                        item.Category = ProcessCategory.WindowsProcess;
                    }
                    else if (!string.IsNullOrEmpty(p.MainWindowTitle))
                    {
                        item.Category = ProcessCategory.Application;
                    }
                    else
                    {
                        item.Category = ProcessCategory.BackgroundProcess;
                    }

                    list.Add(item);
                }
                catch
                {
                    // Process exited during query
                }
                finally
                {
                    p.Dispose();
                }
            }

            // Cleanup stale PIDs from cpu history
            lock (_cpuLock)
            {
                var currentPids = new HashSet<int>(list.Select(x => x.Id));
                var stalePids = _cpuHistory.Keys.Where(pid => !currentPids.Contains(pid)).ToList();
                foreach (var stale in stalePids)
                {
                    _cpuHistory.Remove(stale);
                }
            }

            return list.OrderByDescending(p => p.WorkingSetBytes).ToList();
        }, cancellationToken);
    }

    public async Task<OperationResult> TerminateProcessAsync(int processId, bool force = false, CancellationToken cancellationToken = default)
    {
        if (processId <= 4)
            return OperationResult.Fail("Cannot terminate system kernel processes.");

        try
        {
            var p = Process.GetProcessById(processId);
            if (CriticalProcessNames.Contains(p.ProcessName))
            {
                return OperationResult.Fail($"Process '{p.ProcessName}' is marked as an essential Windows component and cannot be terminated.");
            }

            try
            {
                p.Kill(entireProcessTree: true);
                p.WaitForExit(3000);
                return OperationResult.Ok($"Process {processId} ({p.ProcessName}) was terminated.");
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 5) // Access Denied
            {
                // Elevate on demand
                var adminRes = await _elevationService.ExecuteElevatedAsync(new AdminOperationRequest
                {
                    OperationType = AdminOperationType.TerminateElevatedProcess,
                    Parameters = { ["ProcessId"] = processId.ToString() }
                }, cancellationToken);

                return adminRes;
            }
        }
        catch (ArgumentException)
        {
            return OperationResult.Ok($"Process {processId} has already exited.");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"Failed to terminate process {processId}: {ex.Message}");
        }
    }

    public async Task<OperationResult> RestartProcessAsync(int processId, CancellationToken cancellationToken = default)
    {
        string? exePath = null;
        try
        {
            var p = Process.GetProcessById(processId);
            exePath = p.MainModule?.FileName;
        }
        catch
        {
            // Access denied to path
        }

        if (string.IsNullOrEmpty(exePath))
            return OperationResult.Fail("Unable to retrieve executable path for restart.");

        var termResult = await TerminateProcessAsync(processId, force: true, cancellationToken);
        if (!termResult.IsSuccess)
            return termResult;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true
            });
            return OperationResult.Ok($"Process restarted: {Path.GetFileName(exePath)}");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"Process was terminated but could not be restarted: {ex.Message}");
        }
    }

    public Task<OperationResult> SetPriorityAsync(int processId, ProcessPriorityClass priority, CancellationToken cancellationToken = default)
    {
        try
        {
            using var p = Process.GetProcessById(processId);
            p.PriorityClass = priority;
            return Task.FromResult(OperationResult.Ok($"Process {processId} priority set to {priority}."));
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 5)
        {
            return Task.FromResult(OperationResult.RequiresElevated("Changing priority for this process requires Administrator privileges."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperationResult.Fail($"Failed to set priority: {ex.Message}"));
        }
    }

    private static string TryGetProcessPath(Process p)
    {
        try
        {
            var path = p.MainModule?.FileName;
            if (!string.IsNullOrEmpty(path)) return path;
        }
        catch { }

        try
        {
            var hProcess = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, p.Id);
            if (hProcess != IntPtr.Zero)
            {
                try
                {
                    var sb = new StringBuilder(1024);
                    int size = sb.Capacity;
                    if (NativeMethods.QueryFullProcessImageName(hProcess, 0, sb, ref size))
                    {
                        return sb.ToString();
                    }
                }
                finally
                {
                    NativeMethods.CloseHandle(hProcess);
                }
            }
        }
        catch { }

        return string.Empty;
    }
}
