using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.Core.Interfaces;
using OmniControl.Core.Models.Environment;

namespace OmniControl.App.ViewModels;

public partial class EnvironmentViewModel : ObservableObject
{
    private readonly IEnvironmentManagerService _envService;
    private readonly MainViewModel _mainViewModel;
    private readonly SynchronizationContext? _syncContext;
    private readonly PeriodicTimer _liveTimer = new(TimeSpan.FromSeconds(3));
    private readonly CancellationTokenSource _cts = new();

    [ObservableProperty]
    private EnvironmentVariableScope _currentScope = EnvironmentVariableScope.User;

    [ObservableProperty]
    private string _searchFilter = string.Empty;

    [ObservableProperty]
    private string _newVarName = string.Empty;

    [ObservableProperty]
    private string _newVarValue = string.Empty;

    [ObservableProperty]
    private string _newPathEntry = string.Empty;

    [ObservableProperty]
    private PathItem? _selectedPathItem;

    [ObservableProperty]
    private bool _isLoading;

    public ObservableCollection<EnvVariableItem> Variables { get; } = new();
    public ObservableCollection<PathItem> PathEntries { get; } = new();

    public EnvironmentViewModel(IEnvironmentManagerService envService, MainViewModel mainViewModel)
    {
        _envService = envService;
        _mainViewModel = mainViewModel;
        _syncContext = SynchronizationContext.Current;

        _ = RefreshDataAsync();
        _ = RunLiveMonitoringLoopAsync(_cts.Token);
    }

    private async Task RunLiveMonitoringLoopAsync(CancellationToken token)
    {
        try
        {
            while (await _liveTimer.WaitForNextTickAsync(token))
            {
                if (!string.IsNullOrEmpty(NewVarName) || !string.IsNullOrEmpty(NewVarValue) || !string.IsNullOrEmpty(NewPathEntry))
                {
                    continue;
                }

                var vars = await _envService.GetVariablesAsync(CurrentScope);
                var pathItems = await _envService.GetPathItemsAsync(CurrentScope);
                _syncContext?.Post(_ =>
                {
                    var query = SearchFilter;
                    var filtered = vars.Where(v => string.IsNullOrEmpty(query) ||
                        v.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        v.Value.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

                    if (filtered.Count != Variables.Count)
                    {
                        Variables.Clear();
                        foreach (var v in filtered) Variables.Add(v);
                    }

                    if (pathItems.Count != PathEntries.Count)
                    {
                        PathEntries.Clear();
                        foreach (var p in pathItems) PathEntries.Add(p);
                    }
                }, null);
            }
        }
        catch (OperationCanceledException) { }
    }

    [RelayCommand]
    public async Task RefreshDataAsync()
    {
        IsLoading = true;
        try
        {
            var vars = await _envService.GetVariablesAsync(CurrentScope);
            Variables.Clear();
            foreach (var v in vars)
            {
                if (string.IsNullOrEmpty(SearchFilter) ||
                    v.Name.Contains(SearchFilter, StringComparison.OrdinalIgnoreCase) ||
                    v.Value.Contains(SearchFilter, StringComparison.OrdinalIgnoreCase))
                {
                    Variables.Add(v);
                }
            }

            var paths = await _envService.GetPathItemsAsync(CurrentScope);
            PathEntries.Clear();
            foreach (var p in paths)
            {
                PathEntries.Add(p);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task SwitchScopeAsync(string scopeName)
    {
        CurrentScope = scopeName.Equals("Machine", StringComparison.OrdinalIgnoreCase)
            ? EnvironmentVariableScope.Machine
            : EnvironmentVariableScope.User;

        await RefreshDataAsync();
    }

    [RelayCommand]
    public async Task AddVariableAsync()
    {
        if (string.IsNullOrWhiteSpace(NewVarName))
        {
            _mainViewModel.ShowInfoBar("Variable name cannot be empty.", "Warning");
            return;
        }

        var res = await _envService.SetVariableAsync(NewVarName.Trim(), NewVarValue.Trim(), CurrentScope);
        if (res.IsSuccess)
        {
            _mainViewModel.ShowInfoBar(res.Message, "Success");
            NewVarName = string.Empty;
            NewVarValue = string.Empty;
            await RefreshDataAsync();
        }
        else
        {
            _mainViewModel.ShowInfoBar(res.Message, "Error");
        }
    }

    [RelayCommand]
    public async Task DeleteVariableAsync(EnvVariableItem item)
    {
        if (item == null) return;

        var res = await _envService.DeleteVariableAsync(item.Name, CurrentScope);
        if (res.IsSuccess)
        {
            _mainViewModel.ShowInfoBar(res.Message, "Success");
            await RefreshDataAsync();
        }
        else
        {
            _mainViewModel.ShowInfoBar(res.Message, "Error");
        }
    }

    [RelayCommand]
    public async Task AddPathItemAsync()
    {
        if (string.IsNullOrWhiteSpace(NewPathEntry)) return;

        var p = NewPathEntry.Trim();
        var item = new PathItem
        {
            Index = PathEntries.Count + 1,
            Path = p,
            Scope = CurrentScope,
            Exists = Directory.Exists(p) || File.Exists(p),
            IsDuplicate = PathEntries.Any(x => x.Path.Equals(p, StringComparison.OrdinalIgnoreCase))
        };

        PathEntries.Add(item);
        NewPathEntry = string.Empty;

        await SavePathChangesAsync();
    }

    [RelayCommand]
    public async Task RemovePathItemAsync(PathItem item)
    {
        if (item == null) return;

        PathEntries.Remove(item);
        ReindexPaths();
        await SavePathChangesAsync();
    }

    [RelayCommand]
    public async Task MovePathUpAsync(PathItem item)
    {
        if (item == null) return;
        var idx = PathEntries.IndexOf(item);
        if (idx > 0)
        {
            PathEntries.Move(idx, idx - 1);
            ReindexPaths();
            await SavePathChangesAsync();
        }
    }

    [RelayCommand]
    public async Task MovePathDownAsync(PathItem item)
    {
        if (item == null) return;
        var idx = PathEntries.IndexOf(item);
        if (idx < PathEntries.Count - 1 && idx >= 0)
        {
            PathEntries.Move(idx, idx + 1);
            ReindexPaths();
            await SavePathChangesAsync();
        }
    }

    private void ReindexPaths()
    {
        for (int i = 0; i < PathEntries.Count; i++)
        {
            PathEntries[i].Index = i + 1;
        }
    }

    private async Task SavePathChangesAsync()
    {
        var res = await _envService.SavePathItemsAsync(PathEntries.ToList(), CurrentScope);
        if (res.IsSuccess)
        {
            _mainViewModel.ShowInfoBar("PATH environment variable updated.", "Success");
        }
        else
        {
            _mainViewModel.ShowInfoBar(res.Message, "Error");
        }
    }
}
