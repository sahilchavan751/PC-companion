using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.Core.AdminContract;
using OmniControl.Core.Interfaces;

namespace OmniControl.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IHardwareMonitorService _hardwareMonitorService;
    private readonly IAdminElevationService _adminElevationService;
    private readonly MainViewModel _mainViewModel;

    [ObservableProperty]
    private int _refreshIntervalIndex = 1; // 0: 1000ms, 1: 2000ms (Default), 2: 3000ms, 3: 5000ms

    [ObservableProperty]
    private int _themeIndex = 0; // 0: Dark, 1: Light, 2: System Default

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private bool _minimizeToTray = true;

    [ObservableProperty]
    private bool _backgroundMonitoring = true;

    [ObservableProperty]
    private string _appVersion = "1.0.0 LTS • Production";

    [ObservableProperty]
    private string _elevationStatusText = "Standard User (Elevation on Demand)";

    [ObservableProperty]
    private string _elevationGlyph = "\uE7EF";

    [ObservableProperty]
    private bool _isElevated;

    public SettingsViewModel(
        IHardwareMonitorService hardwareMonitorService,
        IAdminElevationService adminElevationService,
        MainViewModel mainViewModel)
    {
        _hardwareMonitorService = hardwareMonitorService;
        _adminElevationService = adminElevationService;
        _mainViewModel = mainViewModel;

        CheckElevation();
    }

    private void CheckElevation()
    {
        IsElevated = _adminElevationService.IsAppRunningElevated;
        if (IsElevated)
        {
            ElevationStatusText = "Administrator (Full System Privilege)";
            ElevationGlyph = "\uE7EF";
        }
        else
        {
            ElevationStatusText = "Standard User (Least Privilege • UAC Isolated)";
            ElevationGlyph = "\uE730";
        }
    }

    partial void OnRefreshIntervalIndexChanged(int value)
    {
        var intervalMs = value switch
        {
            0 => 1000,
            1 => 2000,
            2 => 3000,
            3 => 5000,
            _ => 2000
        };

        _hardwareMonitorService.StartMonitoring(intervalMs);
        _mainViewModel.ShowInfoBar($"Hardware telemetry polling interval set to {intervalMs} ms.", "Success");
    }

    partial void OnMinimizeToTrayChanged(bool value)
    {
        _mainViewModel.ShowInfoBar(
            value ? "Window close button will now minimize OmniControl to the notification tray." : "Window close button will exit the application.",
            "Informational");
    }

    partial void OnBackgroundMonitoringChanged(bool value)
    {
        _mainViewModel.ShowInfoBar(
            value ? "Background telemetry and LibreHardwareMonitor remain active." : "Background telemetry paused when window is hidden.",
            "Informational");
    }

    [RelayCommand]
    public async Task TestElevationAsync()
    {
        _mainViewModel.ShowInfoBar("Requesting elevated helper authorization via Windows UAC...", "Informational");
        var success = await _adminElevationService.EnsureHelperElevatedAsync();
        if (success)
        {
            _mainViewModel.ShowInfoBar("Administrator helper service authorized and ready for operations.", "Success");
        }
        else
        {
            _mainViewModel.ShowInfoBar("Administrator authorization was cancelled or declined.", "Warning");
        }
    }

    [RelayCommand]
    public void OpenLogsFolder()
    {
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OmniControl");
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _mainViewModel.ShowInfoBar($"Failed to open logs folder: {ex.Message}", "Error");
        }
    }

    [RelayCommand]
    public void ExportDiagnostics()
    {
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OmniControl");
            Directory.CreateDirectory(folder);
            var reportPath = Path.Combine(folder, $"diagnostics_export_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

            var lines = new[]
            {
                "============================================================",
                "OMNICONTROL SYSTEM DIAGNOSTICS & TELEMETRY REPORT",
                $"Crafted by Luxvore Studio • Generated: {DateTime.Now:O}",
                "============================================================",
                $"OS Version: {Environment.OSVersion}",
                $"64-bit OS: {Environment.Is64BitOperatingSystem}",
                $"Machine: {Environment.MachineName}",
                $"Process Elevated: {_adminElevationService.IsAppRunningElevated}",
                $"App Directory: {AppContext.BaseDirectory}",
                $"Polling Interval Index: {RefreshIntervalIndex}",
                $"Minimize to Tray: {MinimizeToTray}",
                $"Background Monitoring: {BackgroundMonitoring}",
                "============================================================"
            };

            File.WriteAllLines(reportPath, lines);
            _mainViewModel.ShowInfoBar($"Diagnostics exported: {reportPath}", "Success");
        }
        catch (Exception ex)
        {
            _mainViewModel.ShowInfoBar($"Export failed: {ex.Message}", "Error");
        }
    }
}
