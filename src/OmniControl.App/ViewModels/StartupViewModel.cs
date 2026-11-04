using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.Core.Interfaces;
using OmniControl.Core.Models.Startup;

namespace OmniControl.App.ViewModels;

public partial class StartupViewModel : ObservableObject
{
    private readonly IStartupManagerService _startupService;
    private readonly MainViewModel _mainViewModel;
    private readonly SynchronizationContext? _syncContext;
    private readonly PeriodicTimer _liveTimer = new(TimeSpan.FromSeconds(10));
    private readonly CancellationTokenSource _cts = new();
    private List<StartupEntry> _allEntries = new();

    [ObservableProperty]
    private string _searchFilter = string.Empty;

    [ObservableProperty]
    private int _enabledCount;

    [ObservableProperty]
    private int _disabledCount;

    [ObservableProperty]
    private bool _isLoading;

    public ObservableCollection<StartupEntry> FilteredEntries { get; } = new();

    public StartupViewModel(IStartupManagerService startupService, MainViewModel mainViewModel)
    {
        _startupService = startupService;
        _mainViewModel = mainViewModel;
        _syncContext = SynchronizationContext.Current;

        _ = RefreshEntriesAsync();
        _ = RunLiveMonitoringLoopAsync(_cts.Token);
    }

    private async Task RunLiveMonitoringLoopAsync(CancellationToken token)
    {
        try
        {
            while (await _liveTimer.WaitForNextTickAsync(token))
            {
                var entries = await _startupService.GetStartupEntriesAsync();
                _syncContext?.Post(_ =>
                {
                    _allEntries = entries;
                    EnabledCount = _allEntries.Count(x => x.IsEnabled);
                    DisabledCount = _allEntries.Count(x => !x.IsEnabled);
                    ApplyFilter();
                }, null);
            }
        }
        catch (OperationCanceledException) { }
    }

    [RelayCommand]
    public async Task RefreshEntriesAsync()
    {
        IsLoading = true;
        try
        {
            _allEntries = await _startupService.GetStartupEntriesAsync();
            EnabledCount = _allEntries.Count(x => x.IsEnabled);
            DisabledCount = _allEntries.Count(x => !x.IsEnabled);
            ApplyFilter();
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSearchFilterChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var query = SearchFilter?.Trim() ?? string.Empty;
        var filtered = _allEntries.AsEnumerable();

        if (!string.IsNullOrEmpty(query))
        {
            filtered = filtered.Where(e =>
                e.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                e.Publisher.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                e.Command.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        var targetList = filtered.ToList();
        if (FilteredEntries.Count == 0)
        {
            foreach (var e in targetList) FilteredEntries.Add(e);
            return;
        }

        var newDict = new Dictionary<string, StartupEntry>(targetList.Count);
        foreach (var e in targetList) newDict[e.Id] = e;

        for (int i = FilteredEntries.Count - 1; i >= 0; i--)
        {
            if (!newDict.ContainsKey(FilteredEntries[i].Id))
            {
                FilteredEntries.RemoveAt(i);
            }
        }

        var existingDict = new Dictionary<string, StartupEntry>(FilteredEntries.Count);
        foreach (var e in FilteredEntries) existingDict[e.Id] = e;

        int insertIdx = 0;
        foreach (var item in targetList)
        {
            if (existingDict.TryGetValue(item.Id, out var existing))
            {
                existing.IsEnabled = item.IsEnabled;
                existing.Impact = item.Impact;
                if (string.IsNullOrEmpty(existing.IconPath) && !string.IsNullOrEmpty(item.IconPath))
                {
                    existing.IconPath = item.IconPath;
                }
            }
            else
            {
                FilteredEntries.Insert(Math.Min(insertIdx, FilteredEntries.Count), item);
            }
            insertIdx++;
        }
    }

    [RelayCommand]
    public async Task SetEntryEnabledAsync((StartupEntry Entry, bool IsEnabled) param)
    {
        await SetEntryStateAsync(param.Entry, param.IsEnabled);
    }

    public async Task SetEntryStateAsync(StartupEntry entry, bool newState)
    {
        if (entry == null) return;
        var prev = entry.IsEnabled;

        var result = await _startupService.SetEntryEnabledAsync(entry, newState);
        if (result.IsSuccess)
        {
            entry.IsEnabled = newState;
            EnabledCount = _allEntries.Count(x => x.IsEnabled);
            DisabledCount = _allEntries.Count(x => !x.IsEnabled);
            _mainViewModel.ShowInfoBar(result.Message, "Success");
        }
        else
        {
            // Revert state
            entry.IsEnabled = prev;
            _mainViewModel.ShowInfoBar(result.Message, "Error");
        }
    }

    [RelayCommand]
    public async Task ToggleEnabledAsync(StartupEntry entry)
    {
        if (entry == null) return;
        await SetEntryStateAsync(entry, !entry.IsEnabled);
    }
}
