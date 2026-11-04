using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.Core.Interfaces;
using OmniControl.Core.Models.Processes;

namespace OmniControl.App.ViewModels;

public partial class ProcessesViewModel : ObservableObject
{
    private readonly IProcessManagerService _processService;
    private readonly MainViewModel _mainViewModel;
    private List<ProcessItem> _allProcesses = new();

    [ObservableProperty]
    private string _searchFilter = string.Empty;

    [ObservableProperty]
    private string _selectedCategory = "All";

    [ObservableProperty]
    private ProcessItem? _selectedProcess;

    [ObservableProperty]
    private int _totalProcessesCount;

    [ObservableProperty]
    private int _appsCount;

    [ObservableProperty]
    private int _backgroundCount;

    [ObservableProperty]
    private int _windowsCount;

    [ObservableProperty]
    private string _sortColumn = "Cpu"; // "Cpu", "Memory", "Name", "Id"

    [ObservableProperty]
    private bool _sortDescending = true;

    [ObservableProperty]
    private bool _isLoading;

    public ObservableCollection<ProcessItem> FilteredProcesses { get; } = new();

    private readonly SynchronizationContext? _syncContext;
    private readonly Timer? _autoRefreshTimer;
    private bool _isDisposed;

    // Track whether a full structural rebuild (sort + filter) is needed,
    // vs a lightweight in-place value update only.
    private bool _needsFullRebuild = true;

    public ProcessesViewModel(IProcessManagerService processService, MainViewModel mainViewModel)
    {
        _processService = processService;
        _mainViewModel = mainViewModel;
        _syncContext = SynchronizationContext.Current;

        _ = RefreshProcessesAsync();

        // 2-second live auto-refresh
        _autoRefreshTimer = new Timer(async _ =>
        {
            if (_isDisposed) return;
            try
            {
                var processes = await _processService.GetProcessesAsync();
                _syncContext?.Post(__ =>
                {
                    if (_isDisposed) return;
                    _allProcesses = processes;
                    UpdateCounts();
                    SyncInPlace();
                }, null);
            }
            catch
            {
                // Suppress background poll errors
            }
        }, null, 2000, 2000);
    }

    private void UpdateCounts()
    {
        TotalProcessesCount = _allProcesses.Count;
        AppsCount = _allProcesses.Count(p => p.Category == ProcessCategory.Application);
        BackgroundCount = _allProcesses.Count(p => p.Category == ProcessCategory.BackgroundProcess);
        WindowsCount = _allProcesses.Count(p => p.Category == ProcessCategory.WindowsProcess);
    }

    [RelayCommand]
    public async Task RefreshProcessesAsync()
    {
        IsLoading = true;
        try
        {
            _allProcesses = await _processService.GetProcessesAsync();
            UpdateCounts();
            _needsFullRebuild = true;
            ApplyFilter();
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void ToggleSort(string column)
    {
        if (string.Equals(SortColumn, column, StringComparison.OrdinalIgnoreCase))
        {
            SortDescending = !SortDescending;
        }
        else
        {
            SortColumn = column;
            SortDescending = column is "Cpu" or "Memory";
        }
        _needsFullRebuild = true;
        ApplyFilter();
    }

    [RelayCommand]
    public void SelectCategory(string category)
    {
        SelectedCategory = category;
    }

    partial void OnSearchFilterChanged(string value)
    {
        _needsFullRebuild = true;
        ApplyFilter();
    }

    partial void OnSelectedCategoryChanged(string value)
    {
        _needsFullRebuild = true;
        ApplyFilter();
    }

    /// <summary>
    /// Lightweight in-place value sync for background auto-refresh ticks.
    /// Updates CPU/Memory/Status values on existing items, adds genuinely new processes,
    /// and removes dead ones — but does NOT re-sort or re-order, preventing scroll jumps.
    /// </summary>
    private void SyncInPlace()
    {
        // Build the set of source items matching current filter criteria (unsorted)
        var sourceItems = GetFilteredItems();
        var sourceById = new Dictionary<int, ProcessItem>(sourceItems.Count);
        foreach (var item in sourceItems)
        {
            sourceById.TryAdd(item.Id, item);
        }

        // Update existing items and remove dead ones
        for (int i = FilteredProcesses.Count - 1; i >= 0; i--)
        {
            var existing = FilteredProcesses[i];
            if (sourceById.TryGetValue(existing.Id, out var fresh))
            {
                // Update volatile properties in-place (triggers INotifyPropertyChanged, no layout shift)
                existing.CpuPercent = fresh.CpuPercent;
                existing.WorkingSetBytes = fresh.WorkingSetBytes;
                existing.ThreadCount = fresh.ThreadCount;
                existing.HandleCount = fresh.HandleCount;
                existing.IsResponding = fresh.IsResponding;
                if (string.IsNullOrEmpty(existing.IconPath) && !string.IsNullOrEmpty(fresh.IconPath))
                {
                    existing.IconPath = fresh.IconPath;
                }
                // Mark as handled
                sourceById.Remove(existing.Id);
            }
            else
            {
                // Process no longer exists or no longer matches filter — remove
                FilteredProcesses.RemoveAt(i);
            }
        }

        // Append genuinely new processes at the end (no re-sorting, minimal disruption)
        foreach (var newItem in sourceById.Values)
        {
            FilteredProcesses.Add(newItem);
        }
    }

    /// <summary>
    /// Returns the filtered (but unsorted) list of processes matching current search + category.
    /// </summary>
    private List<ProcessItem> GetFilteredItems()
    {
        var query = SearchFilter?.Trim() ?? string.Empty;
        var filtered = _allProcesses.AsEnumerable();

        if (!string.IsNullOrEmpty(query))
        {
            filtered = filtered.Where(p =>
                p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                p.Id.ToString().Contains(query) ||
                p.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                p.Description.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        if (SelectedCategory == "Applications")
        {
            filtered = filtered.Where(p => p.Category == ProcessCategory.Application);
        }
        else if (SelectedCategory == "Background")
        {
            filtered = filtered.Where(p => p.Category == ProcessCategory.BackgroundProcess);
        }
        else if (SelectedCategory == "Windows")
        {
            filtered = filtered.Where(p => p.Category == ProcessCategory.WindowsProcess);
        }

        return filtered.ToList();
    }

    /// <summary>
    /// Full structural rebuild: filter + sort + sync.
    /// Only called on initial load and explicit user actions (sort/filter/category change).
    /// </summary>
    private void ApplyFilter()
    {
        var targetList = GetFilteredItems();

        // Apply Sorting
        targetList = SortColumn switch
        {
            "Cpu" => SortDescending ? targetList.OrderByDescending(p => p.CpuPercent).ToList() : targetList.OrderBy(p => p.CpuPercent).ToList(),
            "Memory" => SortDescending ? targetList.OrderByDescending(p => p.WorkingSetBytes).ToList() : targetList.OrderBy(p => p.WorkingSetBytes).ToList(),
            "Name" => SortDescending ? targetList.OrderByDescending(p => p.DisplayName).ToList() : targetList.OrderBy(p => p.DisplayName).ToList(),
            "Id" => SortDescending ? targetList.OrderByDescending(p => p.Id).ToList() : targetList.OrderBy(p => p.Id).ToList(),
            _ => targetList
        };

        if (FilteredProcesses.Count == 0 || _needsFullRebuild)
        {
            // Full replace — clear and repopulate
            FilteredProcesses.Clear();
            foreach (var p in targetList)
            {
                FilteredProcesses.Add(p);
            }
            _needsFullRebuild = false;
            return;
        }

        // Shouldn't reach here during background ticks (those use SyncInPlace),
        // but keep as a fallback for edge cases.
        FilteredProcesses.Clear();
        foreach (var p in targetList)
        {
            FilteredProcesses.Add(p);
        }
    }

    [RelayCommand]
    public async Task EndTaskAsync(ProcessItem? process)
    {
        var target = process ?? SelectedProcess;
        if (target == null) return;

        if (target.IsCritical)
        {
            _mainViewModel.ShowInfoBar($"Refusing to terminate '{target.Name}' because it is a protected Windows system component.", "Error");
            return;
        }

        var result = await _processService.TerminateProcessAsync(target.Id);
        if (result.IsSuccess)
        {
            _mainViewModel.ShowInfoBar(result.Message, "Success");
            await RefreshProcessesAsync();
        }
        else
        {
            _mainViewModel.ShowInfoBar(result.Message, "Error");
        }
    }

    [RelayCommand]
    public async Task RestartTaskAsync(ProcessItem? process)
    {
        var target = process ?? SelectedProcess;
        if (target == null) return;

        var result = await _processService.RestartProcessAsync(target.Id);
        if (result.IsSuccess)
        {
            _mainViewModel.ShowInfoBar(result.Message, "Success");
            await RefreshProcessesAsync();
        }
        else
        {
            _mainViewModel.ShowInfoBar(result.Message, "Error");
        }
    }

    [RelayCommand]
    public async Task SetPriorityAsync((ProcessItem Process, ProcessPriorityClass Priority) param)
    {
        var result = await _processService.SetPriorityAsync(param.Process.Id, param.Priority);
        if (result.IsSuccess)
        {
            param.Process.Priority = param.Priority;
            _mainViewModel.ShowInfoBar(result.Message, "Success");
        }
        else
        {
            _mainViewModel.ShowInfoBar(result.Message, "Warning");
        }
    }

    [RelayCommand]
    public void OpenFileLocation(ProcessItem? process)
    {
        var target = process ?? SelectedProcess;
        if (target != null && !string.IsNullOrEmpty(target.ExecutablePath))
        {
            try
            {
                Process.Start("explorer.exe", $"/select,\"{target.ExecutablePath}\"");
            }
            catch (Exception ex)
            {
                _mainViewModel.ShowInfoBar($"Failed to open file location: {ex.Message}", "Error");
            }
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _autoRefreshTimer?.Dispose();
    }
}
