using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.Core.Interfaces;
using OmniControl.Core.Models.Hardware;

namespace OmniControl.App.ViewModels;

public partial class HardwareViewModel : ObservableObject
{
    private readonly IHardwareMonitorService _hardwareMonitorService;
    private readonly ISystemInfoService _systemInfoService;

    [ObservableProperty]
    private string _cpuName = "Loading...";

    [ObservableProperty]
    private string _motherboardName = string.Empty;

    [ObservableProperty]
    private string _biosVersion = string.Empty;

    [ObservableProperty]
    private string _ramDetails = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private int _categoryFilterIndex = 0; // 0: All, 1: CPU, 2: GPU, 3: Storage, 4: Motherboard

    [ObservableProperty]
    private int _totalSensorCount;

    public ObservableCollection<HardwareDeviceGroup> TemperatureGroups { get; } = new();
    public ObservableCollection<HardwareDeviceGroup> FilteredTemperatureGroups { get; } = new();
    public ObservableCollection<HardwareDeviceGroup> ClockGroups { get; } = new();

    private readonly SynchronizationContext? _syncContext;
    private bool _isDisposed;

    public HardwareViewModel(IHardwareMonitorService hardwareMonitorService, ISystemInfoService systemInfoService)
    {
        _hardwareMonitorService = hardwareMonitorService;
        _systemInfoService = systemInfoService;
        _syncContext = SynchronizationContext.Current;

        _hardwareMonitorService.MetricsUpdated += OnMetricsUpdated;
        _hardwareMonitorService.StartMonitoring(2000);

        _ = RefreshHardwareDetailsAsync();
    }

    private void OnMetricsUpdated(object? sender, HardwareUpdateEventArgs e)
    {
        _syncContext?.Post(_ =>
        {
            if (_isDisposed) return;
            UpdateSensorCollections(e.Sensors);
        }, null);
    }

    partial void OnCategoryFilterIndexChanged(int value)
    {
        ApplyFilter();
    }

    [RelayCommand]
    public async Task RefreshHardwareDetailsAsync()
    {
        IsLoading = true;
        try
        {
            var overview = await _systemInfoService.GetSystemOverviewAsync();
            CpuName = overview.Cpu.Name;
            MotherboardName = string.IsNullOrWhiteSpace(overview.Motherboard.Product)
                ? "Standard Motherboard"
                : $"{overview.Motherboard.Manufacturer} {overview.Motherboard.Product}";
            BiosVersion = $"BIOS: {overview.Motherboard.BiosVersion} ({overview.Motherboard.BiosDate})";
            RamDetails = $"{overview.Ram.MemoryType} • {overview.Ram.MemorySpeedMhz} MHz • {overview.Ram.TotalPhysicalBytes / (1024 * 1024 * 1024)} GB Total";

            UpdateSensorCollections(_hardwareMonitorService.GetAllSensors());
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void UpdateSensorCollections(IReadOnlyList<SensorMetric> sensors)
    {
        // Filter out non-temperature distance to TjMax sensors (irrelevant throttle headroom calculation)
        var validTemps = sensors
            .Where(s => s.Category.Equals("Temperature", StringComparison.OrdinalIgnoreCase) &&
                        !s.Name.Contains("Distance to TjMax", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var validClocks = sensors
            .Where(s => s.Category.Equals("Clock", StringComparison.OrdinalIgnoreCase))
            .ToList();

        TotalSensorCount = validTemps.Count + validClocks.Count;

        SyncTemperatureGroups(validTemps);
        SyncClockGroups(validClocks);
        ApplyFilter();
    }

    private void SyncTemperatureGroups(List<SensorMetric> temps)
    {
        // Group by hardware device
        var grouped = temps.GroupBy(t => DetermineDeviceCategoryAndKey(t));

        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in grouped)
        {
            var (category, key, displayName, glyph) = group.Key;
            seenKeys.Add(key);

            var existingGroup = TemperatureGroups.FirstOrDefault(g => g.DeviceKey.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (existingGroup == null)
            {
                existingGroup = new HardwareDeviceGroup
                {
                    DeviceKey = key,
                    DeviceCategory = category,
                    DeviceName = displayName,
                    Glyph = glyph
                };
                TemperatureGroups.Add(existingGroup);
            }

            // Sync sensors in this group
            SyncSensorsInGroup(existingGroup, group.ToList());
        }

        // Remove groups that no longer exist
        for (int i = TemperatureGroups.Count - 1; i >= 0; i--)
        {
            if (!seenKeys.Contains(TemperatureGroups[i].DeviceKey))
            {
                TemperatureGroups.RemoveAt(i);
            }
        }
    }

    private void SyncSensorsInGroup(HardwareDeviceGroup group, List<SensorMetric> sensors)
    {
        var primaryCandidate = sensors.FirstOrDefault(s => s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase))
            ?? sensors.FirstOrDefault(s => s.Name.Contains("Core Max", StringComparison.OrdinalIgnoreCase))
            ?? sensors.FirstOrDefault(s => s.Name.Contains("Core Average", StringComparison.OrdinalIgnoreCase))
            ?? sensors.FirstOrDefault(s => s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
            ?? sensors.FirstOrDefault();

        if (primaryCandidate != null && primaryCandidate.Value.HasValue)
        {
            var val = primaryCandidate.Value.Value;
            group.PrimaryMetricValue = $"{val:0.#} °C";
            group.PrimaryMetricLabel = primaryCandidate.Name;

            if (val < 60)
            {
                group.StatusColor = "#00E676";
                group.StatusText = "Optimal";
            }
            else if (val < 80)
            {
                group.StatusColor = "#FFB300";
                group.StatusText = "Warm";
            }
            else
            {
                group.StatusColor = "#FF3D57";
                group.StatusText = "High Temp";
            }
        }

        // Separate core-specific detailed sensors from summary sensors
        var isCpu = group.DeviceCategory == "CPU";
        var primarySensors = new List<SensorMetric>();
        var detailSensors = new List<SensorMetric>();

        foreach (var s in sensors)
        {
            if (isCpu && (s.Name.StartsWith("CPU Core #", StringComparison.OrdinalIgnoreCase) || s.Name.StartsWith("Core #", StringComparison.OrdinalIgnoreCase)))
            {
                detailSensors.Add(s);
            }
            else
            {
                primarySensors.Add(s);
            }
        }

        // Sync PrimarySensors in-place
        SyncMetricCollection(group.PrimarySensors, primarySensors);

        // Sync DetailSensors in-place
        SyncMetricCollection(group.DetailSensors, detailSensors);
        group.HasDetails = group.DetailSensors.Count > 0;
    }

    private void SyncClockGroups(List<SensorMetric> clocks)
    {
        var grouped = clocks.GroupBy(c => DetermineDeviceCategoryAndKey(c));
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in grouped)
        {
            var (category, key, displayName, glyph) = group.Key;
            seenKeys.Add(key);

            var existingGroup = ClockGroups.FirstOrDefault(g => g.DeviceKey.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (existingGroup == null)
            {
                existingGroup = new HardwareDeviceGroup
                {
                    DeviceKey = key,
                    DeviceCategory = category,
                    DeviceName = displayName,
                    Glyph = glyph,
                    PrimaryMetricLabel = "Clock Frequency"
                };
                ClockGroups.Add(existingGroup);
            }

            var primaryClock = group.FirstOrDefault(s => s.Name.Contains("Core #1", StringComparison.OrdinalIgnoreCase))
                ?? group.FirstOrDefault(s => s.Name.Contains("Core Max", StringComparison.OrdinalIgnoreCase))
                ?? group.FirstOrDefault(s => s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                ?? group.FirstOrDefault();

            if (primaryClock != null && primaryClock.Value.HasValue)
            {
                existingGroup.PrimaryMetricValue = $"{primaryClock.Value.Value:0} MHz";
                existingGroup.PrimaryMetricLabel = primaryClock.Name;
                existingGroup.StatusColor = "#00D4FF";
                existingGroup.StatusText = "Active";
            }

            var isCpu = existingGroup.DeviceCategory == "CPU";
            var primaryClocks = new List<SensorMetric>();
            var detailClocks = new List<SensorMetric>();

            foreach (var s in group)
            {
                if (isCpu && (s.Name.StartsWith("CPU Core #", StringComparison.OrdinalIgnoreCase) || s.Name.StartsWith("Core #", StringComparison.OrdinalIgnoreCase)))
                {
                    detailClocks.Add(s);
                }
                else
                {
                    primaryClocks.Add(s);
                }
            }

            SyncMetricCollection(existingGroup.PrimarySensors, primaryClocks);
            SyncMetricCollection(existingGroup.DetailSensors, detailClocks);
            existingGroup.HasDetails = existingGroup.DetailSensors.Count > 0;
        }

        for (int i = ClockGroups.Count - 1; i >= 0; i--)
        {
            if (!seenKeys.Contains(ClockGroups[i].DeviceKey))
            {
                ClockGroups.RemoveAt(i);
            }
        }
    }

    private static void SyncMetricCollection(ObservableCollection<SensorMetric> target, List<SensorMetric> incoming)
    {
        var incomingByName = incoming.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);

        // Update existing or remove departed
        for (int i = target.Count - 1; i >= 0; i--)
        {
            var item = target[i];
            if (incomingByName.TryGetValue(item.Name, out var inc))
            {
                item.Value = inc.Value;
                incomingByName.Remove(item.Name);
            }
            else
            {
                target.RemoveAt(i);
            }
        }

        // Add any new items
        foreach (var remaining in incomingByName.Values)
        {
            target.Add(remaining);
        }
    }

    private static (string Category, string Key, string DisplayName, string Glyph) DetermineDeviceCategoryAndKey(SensorMetric s)
    {
        var hwName = string.IsNullOrWhiteSpace(s.HardwareName) ? "System Hardware" : s.HardwareName;
        var lower = hwName.ToLowerInvariant();
        var sensorLower = s.Name.ToLowerInvariant();

        if (lower.Contains("cpu") || lower.Contains("processor") || lower.Contains("core") || lower.Contains("intel core") || lower.Contains("amd ryzen") || sensorLower.Contains("cpu"))
        {
            return ("CPU", $"cpu_{hwName}", hwName, "\uE950");
        }
        if (lower.Contains("gpu") || lower.Contains("nvidia") || lower.Contains("geforce") || lower.Contains("radeon") || lower.Contains("rtx") || lower.Contains("gtx") || lower.Contains("arc") || sensorLower.Contains("gpu"))
        {
            return ("GPU", $"gpu_{hwName}", hwName, "\uE790");
        }
        if (lower.Contains("disk") || lower.Contains("drive") || lower.Contains("nvme") || lower.Contains("ssd") || lower.Contains("samsung") || lower.Contains("crucial") || lower.Contains("kingston") || lower.Contains("wdc") || lower.Contains("seagate"))
        {
            return ("STORAGE", $"storage_{hwName}", hwName, "\uEDA2");
        }

        return ("MOTHERBOARD", $"mb_{hwName}", hwName, "\uE8EA");
    }

    private void ApplyFilter()
    {
        FilteredTemperatureGroups.Clear();

        string? requiredCategory = CategoryFilterIndex switch
        {
            1 => "CPU",
            2 => "GPU",
            3 => "STORAGE",
            4 => "MOTHERBOARD",
            _ => null
        };

        foreach (var group in TemperatureGroups)
        {
            if (requiredCategory == null || group.DeviceCategory.Equals(requiredCategory, StringComparison.OrdinalIgnoreCase))
            {
                FilteredTemperatureGroups.Add(group);
            }
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _hardwareMonitorService.MetricsUpdated -= OnMetricsUpdated;
    }
}
