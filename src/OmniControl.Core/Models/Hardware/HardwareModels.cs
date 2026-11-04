using CommunityToolkit.Mvvm.ComponentModel;

namespace OmniControl.Core.Models.Hardware;

public class CpuInfo
{
    public string Name { get; set; } = "Unknown CPU";
    public string Manufacturer { get; set; } = string.Empty;
    public string Architecture { get; set; } = string.Empty;
    public int CoreCount { get; set; }
    public int LogicalProcessorCount { get; set; }
    public double CurrentFrequencyGhz { get; set; }
    public double BaseFrequencyGhz { get; set; }
    public double UtilizationPercent { get; set; }
    public double? TemperatureCelsius { get; set; }
    public double? PackagePowerWatts { get; set; }
    public List<double> CoreUtilizations { get; set; } = new();
    public List<double?> CoreTemperatures { get; set; } = new();
}

public class GpuInfo
{
    public string Name { get; set; } = "Unknown GPU";
    public string Manufacturer { get; set; } = string.Empty;
    public string DriverVersion { get; set; } = string.Empty;
    public double? UtilizationPercent { get; set; }
    public long TotalVramBytes { get; set; }
    public long UsedVramBytes { get; set; }
    public double? TemperatureCelsius { get; set; }
    public double? CoreClockMhz { get; set; }
    public double? MemoryClockMhz { get; set; }
    public double? FanRpm { get; set; }
    public double VramUsagePercent => TotalVramBytes > 0 ? (double)UsedVramBytes / TotalVramBytes * 100.0 : 0.0;
}

public class RamInfo
{
    public ulong TotalPhysicalBytes { get; set; }
    public ulong AvailablePhysicalBytes { get; set; }
    public ulong UsedPhysicalBytes => TotalPhysicalBytes > AvailablePhysicalBytes ? TotalPhysicalBytes - AvailablePhysicalBytes : 0;
    public double UsagePercent => TotalPhysicalBytes > 0 ? (double)UsedPhysicalBytes / TotalPhysicalBytes * 100.0 : 0.0;
    public ulong TotalCommittedBytes { get; set; }
    public ulong LimitCommittedBytes { get; set; }
    public uint MemorySpeedMhz { get; set; }
    public string MemoryType { get; set; } = "DDR";
    public int ModuleSlotsUsed { get; set; }
    public int TotalModuleSlots { get; set; }
}

public class DiskDriveInfo
{
    public string DeviceId { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string DriveLetter { get; set; } = string.Empty;
    public string VolumeLabel { get; set; } = string.Empty;
    public string FileSystem { get; set; } = string.Empty;
    public string InterfaceType { get; set; } = string.Empty;
    public long TotalBytes { get; set; }
    public long FreeBytes { get; set; }
    public long UsedBytes => TotalBytes > FreeBytes ? TotalBytes - FreeBytes : 0;
    public double UsagePercent => TotalBytes > 0 ? (double)UsedBytes / TotalBytes * 100.0 : 0.0;
    public double? ReadSpeedBps { get; set; }
    public double? WriteSpeedBps { get; set; }
    public double? ActiveTimePercent { get; set; }
    public double? TemperatureCelsius { get; set; }
    public bool IsSystemDrive { get; set; }

    public string TotalFormatted => FormatBytes(TotalBytes);
    public string FreeFormatted => FormatBytes(FreeBytes);
    public string UsedFormatted => FormatBytes(UsedBytes);
    public string UsagePercentFormatted => $"{UsagePercent:F0}%";

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1 && counter < suffixes.Length - 1)
        {
            number /= 1024;
            counter++;
        }
        return $"{number:n1} {suffixes[counter]}";
    }
}

public class NetworkAdapterMetric
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string MacAddress { get; set; } = string.Empty;
    public string InterfaceType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Ipv4Address { get; set; } = string.Empty;
    public string Ipv6Address { get; set; } = string.Empty;
    public string Gateway { get; set; } = string.Empty;
    public string DnsServers { get; set; } = string.Empty;
    public long BytesReceived { get; set; }
    public long BytesSent { get; set; }
    public double DownloadSpeedBps { get; set; }
    public double UploadSpeedBps { get; set; }
    public bool IsConnected { get; set; }
}

public class BatteryInfo
{
    public bool HasBattery { get; set; }
    public int EstimatedChargeRemainingPercent { get; set; }
    public bool IsCharging { get; set; }
    public string PowerStatus { get; set; } = "AC Connected";
    public TimeSpan? EstimatedRunTime { get; set; }
}

public class PowerPlanInfo
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsAcActive { get; set; }
    public bool IsDcActive { get; set; }
    public string PowerCondition { get; set; } = string.Empty;
}

public class PowerStatusSummary
{
    public bool IsOnAcPower { get; set; } = true;
    public string CurrentPowerSourceText { get; set; } = "Plugged In (AC Power)";
    public string EffectiveActivePlanName { get; set; } = "Balanced";
    public string AcPlanName { get; set; } = "Best Performance";
    public string DcPlanName { get; set; } = "Balanced";
    public List<PowerPlanInfo> Plans { get; set; } = new();
}

public class MotherboardInfo
{
    public string Manufacturer { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public string BiosVersion { get; set; } = string.Empty;
    public string BiosDate { get; set; } = string.Empty;
}

public class SensorMetric : ObservableObject
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty; // Temperature, Fan, Clock, Voltage, Load, Power

    private double? _value;
    public double? Value
    {
        get => _value;
        set
        {
            if (SetProperty(ref _value, value))
            {
                OnPropertyChanged(nameof(FormattedValue));
                OnPropertyChanged(nameof(IsSupported));
            }
        }
    }

    public string Unit { get; set; } = string.Empty;
    public string HardwareName { get; set; } = string.Empty;
    public bool IsSupported => Value.HasValue;
    public string FormattedValue => Value.HasValue ? $"{Value.Value:0.#} {Unit}" : "--";
}

public class SystemOverviewInfo
{
    public string OsName { get; set; } = string.Empty;
    public string OsVersion { get; set; } = string.Empty;
    public string OsBuild { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public TimeSpan Uptime { get; set; }
    public bool IsRunningAsAdmin { get; set; }
    public CpuInfo Cpu { get; set; } = new();
    public List<GpuInfo> Gpus { get; set; } = new();
    public RamInfo Ram { get; set; } = new();
    public List<DiskDriveInfo> Disks { get; set; } = new();
    public List<NetworkAdapterMetric> NetworkAdapters { get; set; } = new();
    public BatteryInfo Battery { get; set; } = new();
    public MotherboardInfo Motherboard { get; set; } = new();
}
