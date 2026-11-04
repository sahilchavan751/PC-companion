using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.Core.Interfaces;
using OmniControl.Core.Models.Services;

namespace OmniControl.App.ViewModels;

public partial class ServicesViewModel : ObservableObject
{
    private readonly IWindowsServicesService _serviceService;
    private readonly MainViewModel _mainViewModel;
    private List<WindowsServiceItem> _allServices = new();

    [ObservableProperty]
    private string _searchFilter = string.Empty;

    [ObservableProperty]
    private string _statusFilter = "All";

    [ObservableProperty]
    private WindowsServiceItem? _selectedService;

    [ObservableProperty]
    private int _runningCount;

    [ObservableProperty]
    private int _stoppedCount;

    [ObservableProperty]
    private bool _isLoading;

    public ObservableCollection<WindowsServiceItem> FilteredServices { get; } = new();

    private readonly SynchronizationContext? _syncContext;
    private readonly Timer? _autoRefreshTimer;
    private bool _isDisposed;

    public ServicesViewModel(IWindowsServicesService serviceService, MainViewModel mainViewModel)
    {
        _serviceService = serviceService;
        _mainViewModel = mainViewModel;
        _syncContext = SynchronizationContext.Current;

        _ = RefreshServicesAsync();

        // 3-second live auto-refresh
        _autoRefreshTimer = new Timer(async _ =>
        {
            if (_isDisposed) return;
            try
            {
                var services = await _serviceService.GetServicesAsync();
                _syncContext?.Post(__ =>
                {
                    if (_isDisposed) return;
                    _allServices = services;
                    RunningCount = services.Count(s => s.State == WindowsServiceState.Running);
                    StoppedCount = services.Count(s => s.State == WindowsServiceState.Stopped);
                    ApplyFilter();
                }, null);
            }
            catch
            {
                // Suppress background poll errors
            }
        }, null, 2000, 2000);
    }

    [RelayCommand]
    public async Task RefreshServicesAsync()
    {
        IsLoading = true;
        try
        {
            _allServices = await _serviceService.GetServicesAsync();
            RunningCount = _allServices.Count(s => s.State == WindowsServiceState.Running);
            StoppedCount = _allServices.Count(s => s.State == WindowsServiceState.Stopped);
            ApplyFilter();
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSearchFilterChanged(string value) => ApplyFilter();
    partial void OnStatusFilterChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var query = SearchFilter?.Trim() ?? string.Empty;
        var filtered = _allServices.AsEnumerable();

        if (!string.IsNullOrEmpty(query))
        {
            filtered = filtered.Where(s =>
                s.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                s.ServiceName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                s.Description.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        if (StatusFilter == "Running")
        {
            filtered = filtered.Where(s => s.State == WindowsServiceState.Running);
        }
        else if (StatusFilter == "Stopped")
        {
            filtered = filtered.Where(s => s.State == WindowsServiceState.Stopped);
        }

        var targetList = filtered.ToList();
        if (FilteredServices.Count == 0)
        {
            foreach (var s in targetList) FilteredServices.Add(s);
            return;
        }

        var newDict = new Dictionary<string, WindowsServiceItem>(targetList.Count);
        foreach (var s in targetList) newDict[s.ServiceName] = s;

        for (int i = FilteredServices.Count - 1; i >= 0; i--)
        {
            if (!newDict.ContainsKey(FilteredServices[i].ServiceName))
            {
                FilteredServices.RemoveAt(i);
            }
        }

        var existingDict = new Dictionary<string, WindowsServiceItem>(FilteredServices.Count);
        foreach (var s in FilteredServices) existingDict[s.ServiceName] = s;

        int insertIdx = 0;
        foreach (var item in targetList)
        {
            if (existingDict.TryGetValue(item.ServiceName, out var existing))
            {
                existing.State = item.State;
            }
            else
            {
                FilteredServices.Insert(Math.Min(insertIdx, FilteredServices.Count), item);
            }
            insertIdx++;
        }
    }

    [RelayCommand]
    public async Task StartServiceAsync(WindowsServiceItem service)
    {
        if (service == null) return;

        var res = await _serviceService.StartServiceAsync(service.ServiceName);
        if (res.IsSuccess)
        {
            service.State = WindowsServiceState.Running;
            _mainViewModel.ShowInfoBar(res.Message, "Success");
        }
        else
        {
            _mainViewModel.ShowInfoBar(res.Message, "Error");
        }
    }

    [RelayCommand]
    public async Task StopServiceAsync(WindowsServiceItem service)
    {
        if (service == null) return;

        if (service.IsEssentialService)
        {
            _mainViewModel.ShowInfoBar($"Refusing to stop '{service.DisplayName}' because it is an essential Windows service.", "Error");
            return;
        }

        var res = await _serviceService.StopServiceAsync(service.ServiceName);
        if (res.IsSuccess)
        {
            service.State = WindowsServiceState.Stopped;
            _mainViewModel.ShowInfoBar(res.Message, "Success");
        }
        else
        {
            _mainViewModel.ShowInfoBar(res.Message, "Error");
        }
    }

    [RelayCommand]
    public async Task RestartServiceAsync(WindowsServiceItem service)
    {
        if (service == null) return;

        var res = await _serviceService.RestartServiceAsync(service.ServiceName);
        if (res.IsSuccess)
        {
            service.State = WindowsServiceState.Running;
            _mainViewModel.ShowInfoBar(res.Message, "Success");
        }
        else
        {
            _mainViewModel.ShowInfoBar(res.Message, "Error");
        }
    }

    [RelayCommand]
    public async Task ChangeStartupTypeAsync((WindowsServiceItem Service, WindowsServiceStartMode Mode) param)
    {
        var res = await _serviceService.ChangeStartupTypeAsync(param.Service.ServiceName, param.Mode);
        if (res.IsSuccess)
        {
            param.Service.StartMode = param.Mode;
            _mainViewModel.ShowInfoBar(res.Message, "Success");
        }
        else
        {
            _mainViewModel.ShowInfoBar(res.Message, "Error");
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _autoRefreshTimer?.Dispose();
    }
}
