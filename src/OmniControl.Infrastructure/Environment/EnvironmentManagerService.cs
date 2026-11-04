using System.Collections;
using OmniControl.Core.AdminContract;
using OmniControl.Core.Interfaces;
using OmniControl.Core.Models.Environment;
using OmniControl.Core.Models.History;
using OmniControl.Core.Results;
using OmniControl.Infrastructure.WindowsInterop;

namespace OmniControl.Infrastructure.EnvironmentVars;

public class EnvironmentManagerService : IEnvironmentManagerService
{
    private readonly IAdminElevationService _elevationService;
    private readonly IOperationHistoryService _historyService;

    public EnvironmentManagerService(IAdminElevationService elevationService, IOperationHistoryService historyService)
    {
        _elevationService = elevationService;
        _historyService = historyService;
    }

    public Task<List<EnvVariableItem>> GetVariablesAsync(EnvironmentVariableScope scope, CancellationToken cancellationToken = default)
    {
        var target = scope == EnvironmentVariableScope.User ? EnvironmentVariableTarget.User : EnvironmentVariableTarget.Machine;
        var envVars = System.Environment.GetEnvironmentVariables(target);

        var list = new List<EnvVariableItem>();
        foreach (DictionaryEntry entry in envVars)
        {
            var name = entry.Key?.ToString() ?? string.Empty;
            var val = entry.Value?.ToString() ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(name))
            {
                list.Add(new EnvVariableItem
                {
                    Name = name,
                    Value = val,
                    Scope = scope
                });
            }
        }

        return Task.FromResult(list.OrderBy(x => x.Name).ToList());
    }

    public async Task<OperationResult> SetVariableAsync(string name, string value, EnvironmentVariableScope scope, CancellationToken cancellationToken = default)
    {
        var target = scope == EnvironmentVariableScope.User ? EnvironmentVariableTarget.User : EnvironmentVariableTarget.Machine;
        var prevValue = System.Environment.GetEnvironmentVariable(name, target);

        if (scope == EnvironmentVariableScope.User)
        {
            try
            {
                System.Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.User);
                BroadcastEnvironmentChange();

                await _historyService.RecordOperationAsync(new ChangeHistoryRecord
                {
                    OperationName = "SetUserEnvironmentVariable",
                    ModuleName = "Environment",
                    TargetIdentifier = name,
                    PreviousValue = prevValue,
                    NewValue = value,
                    IsSuccess = true,
                    IsReversible = true,
                    Privilege = PrivilegeLevel.StandardUser
                });

                return OperationResult.Ok($"User environment variable '{name}' was set successfully.");
            }
            catch (Exception ex)
            {
                return OperationResult.Fail($"Failed to set user variable: {ex.Message}");
            }
        }
        else
        {
            // System level requires elevation
            var result = await _elevationService.ExecuteElevatedAsync(new AdminOperationRequest
            {
                OperationType = AdminOperationType.SetMachineEnvironmentVariable,
                Parameters =
                {
                    ["Name"] = name,
                    ["Value"] = value
                }
            }, cancellationToken);

            if (result.IsSuccess)
            {
                await _historyService.RecordOperationAsync(new ChangeHistoryRecord
                {
                    OperationName = "SetMachineEnvironmentVariable",
                    ModuleName = "Environment",
                    TargetIdentifier = name,
                    PreviousValue = prevValue,
                    NewValue = value,
                    IsSuccess = true,
                    IsReversible = true,
                    Privilege = PrivilegeLevel.ElevatedAdmin
                });
            }

            return result;
        }
    }

    public async Task<OperationResult> DeleteVariableAsync(string name, EnvironmentVariableScope scope, CancellationToken cancellationToken = default)
    {
        var target = scope == EnvironmentVariableScope.User ? EnvironmentVariableTarget.User : EnvironmentVariableTarget.Machine;
        var prevValue = System.Environment.GetEnvironmentVariable(name, target);

        if (scope == EnvironmentVariableScope.User)
        {
            try
            {
                System.Environment.SetEnvironmentVariable(name, null, EnvironmentVariableTarget.User);
                BroadcastEnvironmentChange();

                await _historyService.RecordOperationAsync(new ChangeHistoryRecord
                {
                    OperationName = "DeleteUserEnvironmentVariable",
                    ModuleName = "Environment",
                    TargetIdentifier = name,
                    PreviousValue = prevValue,
                    NewValue = null,
                    IsSuccess = true,
                    IsReversible = true,
                    Privilege = PrivilegeLevel.StandardUser
                });

                return OperationResult.Ok($"User environment variable '{name}' was deleted.");
            }
            catch (Exception ex)
            {
                return OperationResult.Fail($"Failed to delete user variable: {ex.Message}");
            }
        }
        else
        {
            var result = await _elevationService.ExecuteElevatedAsync(new AdminOperationRequest
            {
                OperationType = AdminOperationType.DeleteMachineEnvironmentVariable,
                Parameters = { ["Name"] = name }
            }, cancellationToken);

            if (result.IsSuccess)
            {
                await _historyService.RecordOperationAsync(new ChangeHistoryRecord
                {
                    OperationName = "DeleteMachineEnvironmentVariable",
                    ModuleName = "Environment",
                    TargetIdentifier = name,
                    PreviousValue = prevValue,
                    NewValue = null,
                    IsSuccess = true,
                    IsReversible = true,
                    Privilege = PrivilegeLevel.ElevatedAdmin
                });
            }

            return result;
        }
    }

    public async Task<List<PathItem>> GetPathItemsAsync(EnvironmentVariableScope scope, CancellationToken cancellationToken = default)
    {
        var vars = await GetVariablesAsync(scope, cancellationToken);
        var pathVar = vars.FirstOrDefault(v => v.IsPath);

        if (pathVar == null || string.IsNullOrWhiteSpace(pathVar.Value))
            return new List<PathItem>();

        var rawPaths = pathVar.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var list = new List<PathItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < rawPaths.Length; i++)
        {
            var p = rawPaths[i];
            var expanded = System.Environment.ExpandEnvironmentVariables(p);
            var exists = Directory.Exists(expanded) || File.Exists(expanded);
            var isDuplicate = !seen.Add(p);

            list.Add(new PathItem
            {
                Index = i + 1,
                Path = p,
                Scope = scope,
                Exists = exists,
                IsDuplicate = isDuplicate
            });
        }

        return list;
    }

    public async Task<OperationResult> SavePathItemsAsync(List<PathItem> items, EnvironmentVariableScope scope, CancellationToken cancellationToken = default)
    {
        var newPathValue = string.Join(";", items.Select(x => x.Path.Trim()).Where(x => !string.IsNullOrEmpty(x)));
        var target = scope == EnvironmentVariableScope.User ? EnvironmentVariableTarget.User : EnvironmentVariableTarget.Machine;
        var prevValue = System.Environment.GetEnvironmentVariable("PATH", target);

        if (scope == EnvironmentVariableScope.User)
        {
            try
            {
                System.Environment.SetEnvironmentVariable("PATH", newPathValue, EnvironmentVariableTarget.User);
                BroadcastEnvironmentChange();

                await _historyService.RecordOperationAsync(new ChangeHistoryRecord
                {
                    OperationName = "UpdateUserPath",
                    ModuleName = "Environment",
                    TargetIdentifier = "PATH",
                    PreviousValue = prevValue,
                    NewValue = newPathValue,
                    IsSuccess = true,
                    IsReversible = true,
                    Privilege = PrivilegeLevel.StandardUser
                });

                return OperationResult.Ok("User PATH was updated successfully.");
            }
            catch (Exception ex)
            {
                return OperationResult.Fail($"Failed to save user PATH: {ex.Message}");
            }
        }
        else
        {
            var result = await _elevationService.ExecuteElevatedAsync(new AdminOperationRequest
            {
                OperationType = AdminOperationType.SetMachinePathItems,
                Parameters = { ["PathValue"] = newPathValue }
            }, cancellationToken);

            if (result.IsSuccess)
            {
                await _historyService.RecordOperationAsync(new ChangeHistoryRecord
                {
                    OperationName = "UpdateMachinePath",
                    ModuleName = "Environment",
                    TargetIdentifier = "PATH",
                    PreviousValue = prevValue,
                    NewValue = newPathValue,
                    IsSuccess = true,
                    IsReversible = true,
                    Privilege = PrivilegeLevel.ElevatedAdmin
                });
            }

            return result;
        }
    }

    public void BroadcastEnvironmentChange()
    {
        try
        {
            NativeMethods.SendMessageTimeout(
                (IntPtr)NativeMethods.HWND_BROADCAST,
                NativeMethods.WM_SETTINGCHANGE,
                UIntPtr.Zero,
                "Environment",
                NativeMethods.SMTO_ABORTIFHUNG,
                2000,
                out _);
        }
        catch
        {
            // Suppress timeout
        }
    }
}
