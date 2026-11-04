using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.Core.Interfaces;
using OmniControl.Core.Models.Hardware;

namespace OmniControl.App.ViewModels;

public partial class OverviewViewModel : ObservableObject, IDisposable
{
    private readonly ISystemInfoService _systemInfoService;
    private readonly IHardwareMonitorService _hardwareMonitorService;
    private readonly SynchronizationContext? _syncContext;
    private bool _isDisposed;

    [ObservableProperty]
    private string _deviceName = string.Empty;

    [ObservableProperty]
    private string _osInfo = string.Empty;

    [ObservableProperty]
    private string _uptimeString = string.Empty;

    [ObservableProperty]
    private double _cpuUsage;

    [ObservableProperty]
    private string _cpuUsageString = "0.0";

    [ObservableProperty]
    private string _cpuName = "Loading...";

    [ObservableProperty]
    private string _cpuDetails = string.Empty;

    [ObservableProperty]
    private string _cpuCoreCountText = string.Empty;

    [ObservableProperty]
    private string _cpuTempString = "N/A";

    [ObservableProperty]
    private double _ramUsage;

    [ObservableProperty]
    private string _ramUsageString = "0.0";

    [ObservableProperty]
    private string _ramDetails = string.Empty;

    [ObservableProperty]
    private string _ramAvailableDetails = string.Empty;

    [ObservableProperty]
    private string _gpuName = "Loading...";

    [ObservableProperty]
    private double _gpuUsage;

    [ObservableProperty]
    private string _gpuUsageString = "0.0";

    [ObservableProperty]
    private string _gpuVramString = string.Empty;

    [ObservableProperty]
    private string _gpuTempString = "N/A";

    [ObservableProperty]
    private string _networkUploadSpeed = "0 KB/s";

    [ObservableProperty]
    private string _networkDownloadSpeed = "0 KB/s";

    [ObservableProperty]
    private string _activeAdapterName = "Ethernet / Wi-Fi";

    [ObservableProperty]
    private double _networkActivityPercent;

    [ObservableProperty]
    private string _batteryStatusString = "AC Connected";

    [ObservableProperty]
    private int _batteryPercent = 100;

    [ObservableProperty]
    private bool _hasBattery;

    [ObservableProperty]
    private bool _isLoading = true;

    public ObservableCollection<DiskDriveInfo> Drives { get; } = new();

    public OverviewViewModel(ISystemInfoService systemInfoService, IHardwareMonitorService hardwareMonitorService)
    {
        _systemInfoService = systemInfoService;
        _hardwareMonitorService = hardwareMonitorService;
        _syncContext = SynchronizationContext.Current;

        _hardwareMonitorService.MetricsUpdated += OnMetricsUpdated;
        _hardwareMonitorService.StartMonitoring(2000);

        _ = InitializeDataAsync();
    }

    [RelayCommand]
    public async Task InitializeDataAsync()
    {
        IsLoading = true;
        try
        {
            var overview = await _systemInfoService.GetSystemOverviewAsync();

            DeviceName = overview.DeviceName;
            OsInfo = $"{overview.OsName} (Build {overview.OsBuild})";
            UptimeString = $"System Uptime: {overview.Uptime.Days}d {overview.Uptime.Hours}h {overview.Uptime.Minutes}m";

            CpuName = overview.Cpu.Name;
            CpuDetails = $"{overview.Cpu.CoreCount} Cores • {overview.Cpu.LogicalProcessorCount} Threads • {overview.Cpu.BaseFrequencyGhz} GHz";
            CpuCoreCountText = $"{overview.Cpu.CoreCount} Cores • {overview.Cpu.LogicalProcessorCount} Threads";
            CpuUsage = Math.Round(overview.Cpu.UtilizationPercent, 1);
            CpuUsageString = $"{CpuUsage:F1}";
            CpuTempString = overview.Cpu.TemperatureCelsius.HasValue ? $"{overview.Cpu.TemperatureCelsius:F0} °C" : "Not exposed";

            RamUsage = Math.Round(overview.Ram.UsagePercent, 1);
            RamUsageString = $"{RamUsage:F1}";
            RamDetails = $"{FormatBytes((long)overview.Ram.UsedPhysicalBytes)} / {FormatBytes((long)overview.Ram.TotalPhysicalBytes)}";
            long freeBytes = (long)(overview.Ram.TotalPhysicalBytes - overview.Ram.UsedPhysicalBytes);
            RamAvailableDetails = FormatBytes(Math.Max(0, freeBytes));

            if (overview.Gpus.Count > 0)
            {
                var gpu = overview.Gpus[0];
                GpuName = gpu.Name;
                GpuUsage = Math.Round(gpu.UtilizationPercent ?? 0.0, 1);
                GpuUsageString = $"{GpuUsage:F1}";
                GpuVramString = gpu.TotalVramBytes > 0 ? $"{FormatBytes(gpu.TotalVramBytes)} Dedicated" : "Shared Memory";
                GpuTempString = gpu.TemperatureCelsius.HasValue ? $"{gpu.TemperatureCelsius:F0} °C" : "Not exposed";
            }
            else
            {
                GpuName = "Integrated Graphics";
                GpuVramString = "System Shared";
                GpuUsageString = "0.0";
            }

            HasBattery = overview.Battery.HasBattery;
            BatteryPercent = overview.Battery.EstimatedChargeRemainingPercent;
            BatteryStatusString = overview.Battery.PowerStatus;

            Drives.Clear();
            foreach (var d in overview.Disks)
            {
                Drives.Add(d);
            }
        }
        catch
        {
            // Suppress initial query errors
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void OnMetricsUpdated(object? sender, HardwareUpdateEventArgs e)
    {
        _syncContext?.Post(_ =>
        {
            CpuUsage = Math.Round(e.Cpu.UtilizationPercent, 1);
            CpuUsageString = $"{CpuUsage:F1}";
            if (e.Cpu.TemperatureCelsius.HasValue)
            {
                CpuTempString = $"{e.Cpu.TemperatureCelsius:F0} °C";
            }

            RamUsage = Math.Round(e.Ram.UsagePercent, 1);
            RamUsageString = $"{RamUsage:F1}";
            RamDetails = $"{FormatBytes((long)e.Ram.UsedPhysicalBytes)} / {FormatBytes((long)e.Ram.TotalPhysicalBytes)}";
            long freeBytes = (long)(e.Ram.TotalPhysicalBytes - e.Ram.UsedPhysicalBytes);
            RamAvailableDetails = FormatBytes(Math.Max(0, freeBytes));

            if (e.Gpus.Count > 0)
            {
                var g = e.Gpus[0];
                GpuName = g.Name;
                if (g.TotalVramBytes > 0) GpuVramString = $"{FormatBytes(g.TotalVramBytes)} Dedicated";
                if (g.UtilizationPercent.HasValue)
                {
                    GpuUsage = Math.Round(g.UtilizationPercent.Value, 1);
                    GpuUsageString = $"{GpuUsage:F1}";
                }
                if (g.TemperatureCelsius.HasValue) GpuTempString = $"{g.TemperatureCelsius:F0} °C";
            }

            if (e.NetworkAdapters.Count > 0)
            {
                var connectedNics = e.NetworkAdapters.Where(n => n.IsConnected).ToList();
                var primaryNic = connectedNics.OrderByDescending(n => n.DownloadSpeedBps + n.UploadSpeedBps).FirstOrDefault() 
                                 ?? e.NetworkAdapters[0];

                double totalDl = connectedNics.Count > 0 ? connectedNics.Sum(n => n.DownloadSpeedBps) : primaryNic.DownloadSpeedBps;
                double totalUl = connectedNics.Count > 0 ? connectedNics.Sum(n => n.UploadSpeedBps) : primaryNic.UploadSpeedBps;

                NetworkDownloadSpeed = FormatSpeed(totalDl);
                NetworkUploadSpeed = FormatSpeed(totalUl);
                ActiveAdapterName = primaryNic.Name;

                double totalSpeed = totalDl + totalUl;
                NetworkActivityPercent = totalSpeed > 0 ? Math.Min(100.0, Math.Max(5.0, (totalSpeed / (10 * 1024 * 1024)) * 100.0)) : 0.0;
            }

            // Sync drive usage changes
            if (Drives.Count == e.Disks.Count)
            {
                for (int i = 0; i < Drives.Count; i++)
                {
                    Drives[i].FreeBytes = e.Disks[i].FreeBytes;
                }
            }
        }, null);
    }

    private static string FormatSpeed(double bytesPerSec)
    {
        if (bytesPerSec >= 1024 * 1024)
            return $"{bytesPerSec / (1024 * 1024):F1} MB/s";
        return $"{bytesPerSec / 1024:F0} KB/s";
    }

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }
        return $"{number:n1} {suffixes[counter]}";
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _hardwareMonitorService.MetricsUpdated -= OnMetricsUpdated;
    }
}
