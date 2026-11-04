using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.Core.Interfaces;
using OmniControl.Core.Models.Hardware;

namespace OmniControl.App.ViewModels;

public partial class PerformanceViewModel : ObservableObject
{
    private readonly IPerformanceControlService _performanceService;
    private readonly ISystemInfoService _systemInfoService;
    private readonly MainViewModel _mainViewModel;

    [ObservableProperty]
    private string _activePlanName = "Balanced";

    [ObservableProperty]
    private string _currentPowerSourceText = "Plugged In (AC Power)";

    [ObservableProperty]
    private string _powerSourceGlyph = "\uE701";

    [ObservableProperty]
    private bool _isOnAcPower = true;

    [ObservableProperty]
    private string _acPlanName = "Best Performance";

    [ObservableProperty]
    private string _dcPlanName = "Balanced";

    [ObservableProperty]
    private double _cpuUtilization;

    [ObservableProperty]
    private double _baseFrequencyGhz;

    [ObservableProperty]
    private double _currentFrequencyGhz;

    [ObservableProperty]
    private int _coreCount;

    [ObservableProperty]
    private int _threadCount;

    [ObservableProperty]
    private bool _isLoading;

    public ObservableCollection<PowerPlanInfo> PowerPlans { get; } = new();

    private readonly SynchronizationContext? _syncContext;
    private readonly Timer? _autoRefreshTimer;
    private bool _isDisposed;

    public PerformanceViewModel(
        IPerformanceControlService performanceService,
        ISystemInfoService systemInfoService,
        MainViewModel mainViewModel)
    {
        _performanceService = performanceService;
        _systemInfoService = systemInfoService;
        _mainViewModel = mainViewModel;
        _syncContext = SynchronizationContext.Current;

        _ = LoadDataAsync();

        // 2-second live auto-refresh
        _autoRefreshTimer = new Timer(async _ =>
        {
            if (_isDisposed) return;
            try
            {
                var cpu = await _systemInfoService.GetCpuInfoAsync();
                var summary = await _performanceService.GetPowerStatusSummaryAsync();

                _syncContext?.Post(__ =>
                {
                    if (_isDisposed) return;
                    CpuUtilization = cpu.UtilizationPercent;
                    CurrentFrequencyGhz = cpu.CurrentFrequencyGhz > 0 ? cpu.CurrentFrequencyGhz : cpu.BaseFrequencyGhz;
                    IsOnAcPower = summary.IsOnAcPower;
                    CurrentPowerSourceText = summary.CurrentPowerSourceText;
                    PowerSourceGlyph = summary.IsOnAcPower ? "\uE701" : "\uE83F";
                    AcPlanName = summary.AcPlanName;
                    DcPlanName = summary.DcPlanName;
                    ActivePlanName = summary.EffectiveActivePlanName;
                }, null);
            }
            catch
            {
                // Suppress background poll errors
            }
        }, null, 2000, 2000);
    }

    [RelayCommand]
    public async Task LoadDataAsync()
    {
        IsLoading = true;
        try
        {
            var cpu = await _systemInfoService.GetCpuInfoAsync();
            CpuUtilization = cpu.UtilizationPercent;
            BaseFrequencyGhz = cpu.BaseFrequencyGhz;
            CurrentFrequencyGhz = cpu.CurrentFrequencyGhz > 0 ? cpu.CurrentFrequencyGhz : cpu.BaseFrequencyGhz;
            CoreCount = cpu.CoreCount;
            ThreadCount = cpu.LogicalProcessorCount;

            var summary = await _performanceService.GetPowerStatusSummaryAsync();
            IsOnAcPower = summary.IsOnAcPower;
            CurrentPowerSourceText = summary.CurrentPowerSourceText;
            PowerSourceGlyph = summary.IsOnAcPower ? "\uE701" : "\uE83F";
            AcPlanName = summary.AcPlanName;
            DcPlanName = summary.DcPlanName;
            ActivePlanName = summary.EffectiveActivePlanName;

            PowerPlans.Clear();
            foreach (var p in summary.Plans)
            {
                PowerPlans.Add(p);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task SelectPowerPlanAsync(PowerPlanInfo plan)
    {
        if (plan == null) return;

        var result = await _performanceService.SetActivePowerPlanAsync(plan.Id);
        if (result.IsSuccess)
        {
            ActivePlanName = plan.Name;
            foreach (var p in PowerPlans)
            {
                p.IsActive = p.Id == plan.Id;
            }
            _mainViewModel.ShowInfoBar(result.Message, "Success");
        }
        else
        {
            _mainViewModel.ShowInfoBar(result.Message, "Error");
        }
    }

    [RelayCommand]
    public async Task ApplyProfileAsync(string profileName)
    {
        var result = await _performanceService.ApplyPerformanceProfileAsync(profileName);
        if (result.IsSuccess)
        {
            await LoadDataAsync();
            _mainViewModel.ShowInfoBar($"Profile '{profileName}' applied.", "Success");
        }
        else
        {
            _mainViewModel.ShowInfoBar(result.Message, "Warning");
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _autoRefreshTimer?.Dispose();
    }
}
