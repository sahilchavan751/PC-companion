using System.Diagnostics;
using System.Text.RegularExpressions;
using OmniControl.Core.AdminContract;
using OmniControl.Core.Interfaces;
using OmniControl.Core.Models.Hardware;
using OmniControl.Core.Models.History;
using OmniControl.Core.Results;
using OmniControl.Infrastructure.WindowsInterop;

namespace OmniControl.Infrastructure.Hardware;

public class PerformanceControlService : IPerformanceControlService
{
    private static readonly Regex PowerSchemeRegex = new(@"GUID:\s*([0-9a-fA-F\-]{36})\s*\(([^)]+)\)(\s*\*?)", RegexOptions.Compiled);
    private readonly IAdminElevationService _elevationService;
    private readonly IOperationHistoryService _historyService;

    public PerformanceControlService(IAdminElevationService elevationService, IOperationHistoryService historyService)
    {
        _elevationService = elevationService;
        _historyService = historyService;
    }

    public async Task<PowerStatusSummary> GetPowerStatusSummaryAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(async () =>
        {
            var summary = new PowerStatusSummary();
            try
            {
                bool isOnAc = true;
                if (NativeMethods.GetSystemPowerStatus(out var powerStatus))
                {
                    isOnAc = powerStatus.ACLineStatus != 0; // 0 = Offline (Battery), 1 = Online (Plugged in)
                }

                summary.IsOnAcPower = isOnAc;
                summary.CurrentPowerSourceText = isOnAc ? "Plugged In (AC Power)" : "On Battery (DC Power)";

                string acOverlay = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes", "ActiveOverlayAcPowerScheme", "")?.ToString() ?? "";
                string dcOverlay = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes", "ActiveOverlayDcPowerScheme", "")?.ToString() ?? "";

                summary.AcPlanName = ResolveOverlayName(acOverlay, "Best Performance");
                summary.DcPlanName = ResolveOverlayName(dcOverlay, "Balanced");
                summary.EffectiveActivePlanName = isOnAc ? summary.AcPlanName : summary.DcPlanName;

                var plans = await GetPowerPlansAsync(cancellationToken);
                foreach (var p in plans)
                {
                    p.IsActive = string.Equals(p.Name, summary.EffectiveActivePlanName, StringComparison.OrdinalIgnoreCase) ||
                                 p.Name.Contains(summary.EffectiveActivePlanName, StringComparison.OrdinalIgnoreCase) ||
                                 summary.EffectiveActivePlanName.Contains(p.Name, StringComparison.OrdinalIgnoreCase);

                    p.IsAcActive = string.Equals(p.Name, summary.AcPlanName, StringComparison.OrdinalIgnoreCase) ||
                                   p.Name.Contains(summary.AcPlanName, StringComparison.OrdinalIgnoreCase) ||
                                   summary.AcPlanName.Contains(p.Name, StringComparison.OrdinalIgnoreCase);

                    p.IsDcActive = string.Equals(p.Name, summary.DcPlanName, StringComparison.OrdinalIgnoreCase) ||
                                   p.Name.Contains(summary.DcPlanName, StringComparison.OrdinalIgnoreCase) ||
                                   summary.DcPlanName.Contains(p.Name, StringComparison.OrdinalIgnoreCase);
                }
                summary.Plans = plans;
            }
            catch
            {
                summary.EffectiveActivePlanName = "Balanced";
            }

            return summary;
        }, cancellationToken);
    }

    private static string ResolveOverlayName(string? guidStr, string fallback)
    {
        if (string.IsNullOrWhiteSpace(guidStr)) return fallback;

        var g = guidStr.Trim().ToLowerInvariant();
        if (g.Contains("ded574b5-45a0-4f42-8737-46345c09c238")) return "Best Performance";
        if (g.Contains("3af9b8d9-7c97-431d-ad78-34a8bfea439f")) return "High Performance";
        if (g.Contains("961cc777-2547-4f9d-8174-7d86181b8a7a")) return "Best Power Efficiency";
        if (g.Contains("00000000-0000-0000-0000-000000000000")) return "Balanced";
        if (g.Contains("381b4222-f694-41f0-9685-ff5bb260df2e")) return "Balanced";
        if (g.Contains("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c")) return "High Performance";
        if (g.Contains("a1841308-3541-4fab-bc81-f71556f20b4a")) return "Power Saver";
        if (g.Contains("e9a42b02-d5df-448d-aa00-03f14749eb61")) return "Ultimate Performance";

        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes\{g}");
            if (key != null)
            {
                var raw = key.GetValue("FriendlyName")?.ToString();
                if (!string.IsNullOrEmpty(raw))
                {
                    var parts = raw.Split(',');
                    return parts.Length > 0 ? parts[^1].Trim() : raw;
                }
            }
        }
        catch { }

        return fallback;
    }

    public async Task<List<PowerPlanInfo>> GetPowerPlansAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var list = new List<PowerPlanInfo>();
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powercfg.exe",
                    Arguments = "/list",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process != null)
                {
                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(3000);

                    var matches = PowerSchemeRegex.Matches(output);
                    foreach (Match match in matches)
                    {
                        if (Guid.TryParse(match.Groups[1].Value, out var guid))
                        {
                            var name = match.Groups[2].Value.Trim();
                            var isActive = match.Groups[3].Value.Contains('*');

                            list.Add(new PowerPlanInfo
                            {
                                Id = guid,
                                Name = name,
                                Description = GetPlanDescription(name),
                                IsActive = isActive
                            });
                        }
                    }
                }
            }
            catch
            {
                // Fallback: Default standard Windows schemes
                list.Add(new PowerPlanInfo
                {
                    Id = new Guid("381b4222-f694-41f0-9685-ff5bb260df2e"),
                    Name = "Balanced",
                    Description = "Automatically balances performance with energy consumption.",
                    IsActive = true
                });
            }

            // Ensure Windows 11 Modern Power Modes are represented if not already in list
            if (!list.Any(p => p.Name.Contains("Best Performance", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("High performance", StringComparison.OrdinalIgnoreCase)))
            {
                list.Add(new PowerPlanInfo
                {
                    Id = new Guid("ded574b5-45a0-4f42-8737-46345c09c238"),
                    Name = "Best Performance",
                    Description = "Windows 11 optimized mode for peak responsiveness and CPU clock boost.",
                    IsActive = false
                });
            }

            if (!list.Any(p => p.Name.Contains("Efficiency", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Power saver", StringComparison.OrdinalIgnoreCase)))
            {
                list.Add(new PowerPlanInfo
                {
                    Id = new Guid("961cc777-2547-4f9d-8174-7d86181b8a7a"),
                    Name = "Best Power Efficiency",
                    Description = "Conserves power, reduces fan noise, and extends battery longevity.",
                    IsActive = false
                });
            }

            return list;
        }, cancellationToken);
    }

    public async Task<OperationResult> SetActivePowerPlanAsync(Guid planId, CancellationToken cancellationToken = default)
    {
        var plans = await GetPowerPlansAsync(cancellationToken);
        var currentActive = plans.FirstOrDefault(p => p.IsActive);
        var targetPlan = plans.FirstOrDefault(p => p.Id == planId);

        if (targetPlan == null)
            return OperationResult.Fail($"Power plan with ID {planId} was not found on this system.");

        bool isOverlay = planId == new Guid("ded574b5-45a0-4f42-8737-46345c09c238") || 
                         planId == new Guid("961cc777-2547-4f9d-8174-7d86181b8a7a");

        bool success = false;
        uint res = 0;

        if (isOverlay)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes", true);
                if (key != null)
                {
                    key.SetValue("ActiveOverlayAcPowerScheme", planId.ToString());
                    key.SetValue("ActiveOverlayDcPowerScheme", planId.ToString());
                    success = true;
                }
            }
            catch
            {
                // Fall through to elevation helper
            }
        }
        else
        {
            res = NativeMethods.PowerSetActiveScheme(IntPtr.Zero, ref planId);
            success = res == 0;
        }

        if (!success)
        {
            // Try via elevated helper
            var adminRes = await _elevationService.ExecuteElevatedAsync(new AdminOperationRequest
            {
                OperationType = AdminOperationType.ApplyPowerPlan,
                Parameters = { ["PlanGuid"] = planId.ToString() }
            }, cancellationToken);

            success = adminRes.IsSuccess;
        }

        if (success)
        {
            NativeMethods.SendMessageTimeout(
                (IntPtr)NativeMethods.HWND_BROADCAST,
                NativeMethods.WM_SETTINGCHANGE,
                UIntPtr.Zero,
                "Environment",
                NativeMethods.SMTO_ABORTIFHUNG,
                1000,
                out _);
        }

        await _historyService.RecordOperationAsync(new ChangeHistoryRecord
        {
            OperationName = "SetActivePowerPlan",
            ModuleName = "Performance",
            TargetIdentifier = planId.ToString(),
            PreviousValue = currentActive?.Id.ToString(),
            NewValue = planId.ToString(),
            IsSuccess = success,
            IsReversible = true,
            Privilege = _elevationService.IsAppRunningElevated ? PrivilegeLevel.ElevatedAdmin : PrivilegeLevel.StandardUser
        });

        return success
            ? OperationResult.Ok($"Active power plan set to '{targetPlan.Name}'.")
            : OperationResult.Fail($"Failed to set active power plan. Error code: {res}");
    }

    public async Task<OperationResult> ApplyPerformanceProfileAsync(string profileName, CancellationToken cancellationToken = default)
    {
        var plans = await GetPowerPlansAsync(cancellationToken);
        var match = plans.FirstOrDefault(p => p.Name.Contains(profileName, StringComparison.OrdinalIgnoreCase));

        if (match != null)
        {
            return await SetActivePowerPlanAsync(match.Id, cancellationToken);
        }

        return OperationResult.Unsupported($"No Windows power scheme matching '{profileName}' was found on this hardware.");
    }

    private static string GetPlanDescription(string name)
    {
        if (name.Contains("Balanced", StringComparison.OrdinalIgnoreCase))
            return "Automatically balances performance with energy consumption on capable hardware.";
        if (name.Contains("High performance", StringComparison.OrdinalIgnoreCase))
            return "Favors performance, but may use more energy.";
        if (name.Contains("Power saver", StringComparison.OrdinalIgnoreCase))
            return "Saves energy by reducing PC performance and screen brightness.";
        if (name.Contains("Ultimate", StringComparison.OrdinalIgnoreCase))
            return "Provides ultimate performance on higher-end PCs.";

        return "Standard Windows system power configuration profile.";
    }
}
