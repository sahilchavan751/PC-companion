using System.Diagnostics;
using Microsoft.Win32;
using OmniControl.Core.AdminContract;
using OmniControl.Core.Interfaces;
using OmniControl.Core.Models.History;
using OmniControl.Core.Models.Startup;
using OmniControl.Core.Results;
using OmniControl.Infrastructure.WindowsInterop;

namespace OmniControl.Infrastructure.Startup;

public class StartupManagerService : IStartupManagerService
{
    private readonly IAdminElevationService _elevationService;
    private readonly IOperationHistoryService _historyService;

    public StartupManagerService(IAdminElevationService elevationService, IOperationHistoryService historyService)
    {
        _elevationService = elevationService;
        _historyService = historyService;
    }

    public async Task<List<StartupEntry>> GetStartupEntriesAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var entries = new List<StartupEntry>();

            // 1. HKCU Run
            ScanRegistryRunKey(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", StartupScope.CurrentUser, entries);

            // 2. HKLM Run
            ScanRegistryRunKey(Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run", StartupScope.LocalMachine, entries);

            // 3. HKLM WOW6432Node Run
            ScanRegistryRunKey(Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", StartupScope.LocalMachine, entries);

            // 4. User Startup Folder
            var userStartup = global::System.Environment.GetFolderPath(global::System.Environment.SpecialFolder.Startup);
            ScanFolderStartup(userStartup, StartupScope.CurrentUser, entries);

            // 5. Common Machine Startup Folder
            var commonStartup = global::System.Environment.GetFolderPath(global::System.Environment.SpecialFolder.CommonStartup);
            ScanFolderStartup(commonStartup, StartupScope.LocalMachine, entries);

            return entries;
        }, cancellationToken);
    }

    public async Task<OperationResult> SetEntryEnabledAsync(StartupEntry entry, bool isEnabled, CancellationToken cancellationToken = default)
    {
        var prev = entry.IsEnabled;

        if (entry.SourceType == StartupSourceType.StartupFolder)
        {
            try
            {
                var filePath = entry.Command;
                if (isEnabled && filePath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                {
                    var activePath = filePath[..^9];
                    File.Move(filePath, activePath, overwrite: true);
                    entry.Command = activePath;
                }
                else if (!isEnabled && !filePath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                {
                    var disabledPath = filePath + ".disabled";
                    File.Move(filePath, disabledPath, overwrite: true);
                    entry.Command = disabledPath;
                }
                entry.IsEnabled = isEnabled;
                return OperationResult.Ok($"Startup shortcut is now {(isEnabled ? "enabled" : "disabled")}.");
            }
            catch (Exception ex)
            {
                return OperationResult.Fail($"Failed to toggle startup file: {ex.Message}");
            }
        }

        // Registry Run entries use StartupApproved mechanism
        if (entry.Scope == StartupScope.CurrentUser)
        {
            try
            {
                const string approvedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
                using var key = Registry.CurrentUser.CreateSubKey(approvedPath, writable: true);
                var bytes = isEnabled
                    ? new byte[] { 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }
                    : new byte[] { 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

                key.SetValue(entry.ValueName, bytes, RegistryValueKind.Binary);
                entry.IsEnabled = isEnabled;

                await _historyService.RecordOperationAsync(new ChangeHistoryRecord
                {
                    OperationName = "ToggleStartupEntry",
                    ModuleName = "Startup",
                    TargetIdentifier = entry.Name,
                    PreviousValue = prev.ToString(),
                    NewValue = isEnabled.ToString(),
                    IsSuccess = true,
                    IsReversible = true,
                    Privilege = PrivilegeLevel.StandardUser
                });

                return OperationResult.Ok($"Startup application '{entry.Name}' is now {(isEnabled ? "enabled" : "disabled")}.");
            }
            catch (Exception ex)
            {
                return OperationResult.Fail($"Failed to update startup state: {ex.Message}");
            }
        }
        else
        {
            // Machine scope requires elevation
            var result = await _elevationService.ExecuteElevatedAsync(new AdminOperationRequest
            {
                OperationType = AdminOperationType.SetMachineStartupEntryState,
                Parameters =
                {
                    ["SubKey"] = entry.RegistryKeyPath,
                    ["ValueName"] = entry.ValueName,
                    ["IsEnabled"] = isEnabled.ToString()
                }
            }, cancellationToken);

            if (result.IsSuccess)
            {
                entry.IsEnabled = isEnabled;
                await _historyService.RecordOperationAsync(new ChangeHistoryRecord
                {
                    OperationName = "ToggleMachineStartupEntry",
                    ModuleName = "Startup",
                    TargetIdentifier = entry.Name,
                    PreviousValue = prev.ToString(),
                    NewValue = isEnabled.ToString(),
                    IsSuccess = true,
                    IsReversible = true,
                    Privilege = PrivilegeLevel.ElevatedAdmin
                });
            }

            return result;
        }
    }

    private static void ScanRegistryRunKey(RegistryKey rootKey, string subKeyPath, StartupScope scope, List<StartupEntry> results)
    {
        try
        {
            using var key = rootKey.OpenSubKey(subKeyPath);
            if (key == null) return;

            var approvedMap = GetStartupApprovedMap(rootKey);

            foreach (var valueName in key.GetValueNames())
            {
                var val = key.GetValue(valueName)?.ToString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(val)) continue;

                var (exePath, args) = ParseCommandAndArgs(val);
                var isEnabled = true;

                if (approvedMap.TryGetValue(valueName, out var approvedBytes))
                {
                    // If first byte is 0x03 or odd, it is disabled
                    if (approvedBytes.Length > 0 && (approvedBytes[0] & 1) != 0)
                    {
                        isEnabled = false;
                    }
                }

                string publisher = string.Empty;
                string? iconPath = null;
                if (File.Exists(exePath))
                {
                    try
                    {
                        var vi = FileVersionInfo.GetVersionInfo(exePath);
                        publisher = vi.CompanyName ?? string.Empty;
                    }
                    catch
                    {
                        // File version read error
                    }

                    iconPath = AppIconExtractor.GetOrExtractIcon(exePath);
                }

                results.Add(new StartupEntry
                {
                    Id = $"{scope}_{valueName}",
                    Name = valueName,
                    Command = val,
                    ExecutablePath = exePath,
                    Arguments = args,
                    Publisher = publisher,
                    Location = $"{rootKey.Name}\\{subKeyPath}",
                    Scope = scope,
                    SourceType = StartupSourceType.RegistryRun,
                    IsEnabled = isEnabled,
                    RegistryKeyPath = subKeyPath,
                    ValueName = valueName,
                    IconPath = iconPath
                });
            }
        }
        catch
        {
            // Suppress registry key read error
        }
    }

    private static Dictionary<string, byte[]> GetStartupApprovedMap(RegistryKey rootKey)
    {
        var map = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var key = rootKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run");
            if (key != null)
            {
                foreach (var name in key.GetValueNames())
                {
                    if (key.GetValue(name) is byte[] b)
                    {
                        map[name] = b;
                    }
                }
            }
        }
        catch
        {
            // Suppress
        }
        return map;
    }

    private static void ScanFolderStartup(string folderPath, StartupScope scope, List<StartupEntry> results)
    {
        if (!Directory.Exists(folderPath)) return;

        try
        {
            foreach (var file in Directory.GetFiles(folderPath))
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext is not (".lnk" or ".url" or ".bat" or ".cmd" or ".disabled"))
                    continue;

                var isEnabled = !ext.Equals(".disabled", StringComparison.OrdinalIgnoreCase);
                var name = Path.GetFileNameWithoutExtension(file);
                var iconPath = AppIconExtractor.GetOrExtractIcon(file);

                results.Add(new StartupEntry
                {
                    Id = $"{scope}_{name}",
                    Name = name,
                    Command = file,
                    ExecutablePath = file,
                    Publisher = "Shortcut / Script",
                    Location = folderPath,
                    Scope = scope,
                    SourceType = StartupSourceType.StartupFolder,
                    IsEnabled = isEnabled,
                    IconPath = iconPath
                });
            }
        }
        catch
        {
            // Suppress
        }
    }

    private static (string ExePath, string Args) ParseCommandAndArgs(string rawCommand)
    {
        var trimmed = global::System.Environment.ExpandEnvironmentVariables(rawCommand).Trim();
        if (trimmed.StartsWith('\"'))
        {
            var nextQuote = trimmed.IndexOf('\"', 1);
            if (nextQuote > 1)
            {
                var exe = trimmed[1..nextQuote];
                var args = trimmed[(nextQuote + 1)..].Trim();
                return (exe, args);
            }
        }

        var exeIndex = trimmed.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeIndex >= 0)
        {
            var exe = trimmed[..(exeIndex + 4)].Trim('\"', ' ');
            var args = trimmed[(exeIndex + 4)..].Trim();
            return (exe, args);
        }

        var firstSpace = trimmed.IndexOf(' ');
        if (firstSpace > 0)
        {
            return (trimmed[..firstSpace], trimmed[firstSpace..].Trim());
        }

        return (trimmed, string.Empty);
    }
}
