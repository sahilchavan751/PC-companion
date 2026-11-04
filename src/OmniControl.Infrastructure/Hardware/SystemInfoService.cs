using System.Collections.Concurrent;
using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;
using OmniControl.Core.Interfaces;
using OmniControl.Core.Models.Hardware;
using OmniControl.Infrastructure.WindowsInterop;

namespace OmniControl.Infrastructure.Hardware;

public class SystemInfoService : ISystemInfoService
{
    private static NativeMethods.FILETIME _prevSysIdle;
    private static NativeMethods.FILETIME _prevSysKernel;
    private static NativeMethods.FILETIME _prevSysUser;
    private static bool _firstCpuSample = true;

    public bool IsRunningAsAdministrator()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    public async Task<SystemOverviewInfo> GetSystemOverviewAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var overview = new SystemOverviewInfo
            {
                DeviceName = global::System.Environment.MachineName,
                UserName = global::System.Environment.UserName,
                OsName = RuntimeInformation.OSDescription,
                OsVersion = global::System.Environment.OSVersion.VersionString,
                OsBuild = global::System.Environment.OSVersion.Version.Build.ToString(),
                Uptime = TimeSpan.FromMilliseconds(global::System.Environment.TickCount64),
                IsRunningAsAdmin = IsRunningAsAdministrator(),
                Cpu = GetCpuInfoInternal(),
                Ram = GetRamInfoInternal(),
                Disks = GetDisksInternal(),
                NetworkAdapters = GetNetworkAdaptersInternal(),
                Battery = GetBatteryInfoInternal(),
                Motherboard = GetMotherboardInternal()
            };

            return overview;
        }, cancellationToken);
    }

    public Task<CpuInfo> GetCpuInfoAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(GetCpuInfoInternal());

    public Task<List<GpuInfo>> GetGpuInfoAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(GetGpuInfoInternal());

    public Task<RamInfo> GetRamInfoAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(GetRamInfoInternal());

    public Task<List<DiskDriveInfo>> GetDisksAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(GetDisksInternal());

    public Task<List<NetworkAdapterMetric>> GetNetworkAdaptersAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(GetNetworkAdaptersInternal());

    public Task<BatteryInfo> GetBatteryInfoAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(GetBatteryInfoInternal());

    private static CpuInfo GetCpuInfoInternal()
    {
        var info = new CpuInfo
        {
            LogicalProcessorCount = global::System.Environment.ProcessorCount,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            UtilizationPercent = CalculateSystemCpuUtilization()
        };

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, Manufacturer, NumberOfCores, MaxClockSpeed, CurrentClockSpeed FROM Win32_Processor");
            foreach (var item in searcher.Get())
            {
                info.Name = item["Name"]?.ToString()?.Trim() ?? "Unknown Processor";
                info.Manufacturer = item["Manufacturer"]?.ToString()?.Trim() ?? string.Empty;
                if (int.TryParse(item["NumberOfCores"]?.ToString(), out var cores))
                    info.CoreCount = cores;
                if (double.TryParse(item["MaxClockSpeed"]?.ToString(), out var mhz))
                    info.BaseFrequencyGhz = Math.Round(mhz / 1000.0, 2);
                if (double.TryParse(item["CurrentClockSpeed"]?.ToString(), out var curMhz))
                    info.CurrentFrequencyGhz = Math.Round(curMhz / 1000.0, 2);
                break;
            }
        }
        catch
        {
            info.Name = global::System.Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "x64 Processor";
        }

        if (info.CoreCount == 0)
            info.CoreCount = Math.Max(1, info.LogicalProcessorCount / 2);

        return info;
    }

    private static double CalculateSystemCpuUtilization()
    {
        if (!NativeMethods.GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
            return 0.0;

        if (_firstCpuSample)
        {
            _prevSysIdle = idleTime;
            _prevSysKernel = kernelTime;
            _prevSysUser = userTime;
            _firstCpuSample = false;
            return 0.0;
        }

        var idle = idleTime.ToULong() - _prevSysIdle.ToULong();
        var kernel = kernelTime.ToULong() - _prevSysKernel.ToULong();
        var user = userTime.ToULong() - _prevSysUser.ToULong();

        _prevSysIdle = idleTime;
        _prevSysKernel = kernelTime;
        _prevSysUser = userTime;

        var total = kernel + user;
        if (total == 0) return 0.0;

        var cpu = (double)(total - idle) * 100.0 / total;
        return Math.Max(0.0, Math.Min(100.0, Math.Round(cpu, 1)));
    }

    private static RamInfo GetRamInfoInternal()
    {
        var ram = new RamInfo();
        var memStatus = new NativeMethods.MEMORYSTATUSEX();
        if (NativeMethods.GlobalMemoryStatusEx(memStatus))
        {
            ram.TotalPhysicalBytes = memStatus.ullTotalPhys;
            ram.AvailablePhysicalBytes = memStatus.ullAvailPhys;
            ram.TotalCommittedBytes = memStatus.ullTotalPageFile - memStatus.ullAvailPageFile;
            ram.LimitCommittedBytes = memStatus.ullTotalPageFile;
        }

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Speed, MemoryType, SMBIOSMemoryType FROM Win32_PhysicalMemory");
            foreach (var item in searcher.Get())
            {
                if (uint.TryParse(item["Speed"]?.ToString(), out var speed) && speed > 0)
                {
                    ram.MemorySpeedMhz = speed;
                    break;
                }
            }
        }
        catch
        {
            // Fallback
        }

        return ram;
    }

    private static List<GpuInfo> GetGpuInfoInternal()
    {
        var list = new List<GpuInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, AdapterCompatibility, DriverVersion, AdapterRAM FROM Win32_VideoController");
            foreach (var item in searcher.Get())
            {
                var name = item["Name"]?.ToString()?.Trim() ?? "GPU";

                // Filter out non-physical / virtual display drivers (e.g. LuminonCore IDDCX, indirect/virtual/mirror displays)
                if (name.Contains("IDDCX", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Remote", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Indirect", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Mirror", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Basic", StringComparison.OrdinalIgnoreCase) && list.Count > 0)
                {
                    continue;
                }

                var gpu = new GpuInfo
                {
                    Name = name,
                    Manufacturer = item["AdapterCompatibility"]?.ToString()?.Trim() ?? string.Empty,
                    DriverVersion = item["DriverVersion"]?.ToString()?.Trim() ?? string.Empty
                };

                if (long.TryParse(item["AdapterRAM"]?.ToString(), out var vram) && vram > 0)
                {
                    gpu.TotalVramBytes = vram;
                }

                list.Add(gpu);
            }

            // Prioritize dedicated GPUs (NVIDIA, GeForce, RTX, GTX, Radeon, AMD) or GPUs with higher dedicated VRAM
            list = list.OrderByDescending(g =>
                g.Name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                g.Name.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
                g.Name.Contains("RTX", StringComparison.OrdinalIgnoreCase) ||
                g.Name.Contains("GTX", StringComparison.OrdinalIgnoreCase) ||
                g.Name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ||
                g.Name.Contains("AMD", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(g => g.TotalVramBytes)
                .ToList();
        }
        catch
        {
            // WMI fallback
        }

        return list;
    }

    private static List<DiskDriveInfo> GetDisksInternal()
    {
        var list = new List<DiskDriveInfo>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady || drive.DriveType != DriveType.Fixed && drive.DriveType != DriveType.Removable)
                    continue;

                list.Add(new DiskDriveInfo
                {
                    DriveLetter = drive.Name.TrimEnd('\\'),
                    VolumeLabel = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Local Disk" : drive.VolumeLabel,
                    FileSystem = drive.DriveFormat,
                    TotalBytes = drive.TotalSize,
                    FreeBytes = drive.AvailableFreeSpace,
                    IsSystemDrive = drive.Name.StartsWith("C:", StringComparison.OrdinalIgnoreCase)
                });
            }
        }
        catch
        {
            // Fallback
        }

        return list;
    }

    private static readonly ConcurrentDictionary<string, (long BytesRecv, long BytesSent, DateTime Timestamp)> _nicSpeedHistory = new();

    private static List<NetworkAdapterMetric> GetNetworkAdaptersInternal()
    {
        var list = new List<NetworkAdapterMetric>();
        var now = DateTime.UtcNow;
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                var ipProps = nic.GetIPProperties();
                var ipv4 = ipProps.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.Address.ToString() ?? string.Empty;
                var ipv6 = ipProps.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)?.Address.ToString() ?? string.Empty;
                var gateway = ipProps.GatewayAddresses.FirstOrDefault()?.Address.ToString() ?? string.Empty;
                var dns = string.Join(", ", ipProps.DnsAddresses.Select(d => d.ToString()));

                var stats = nic.GetIPStatistics();
                double dlSpeed = 0;
                double ulSpeed = 0;

                if (_nicSpeedHistory.TryGetValue(nic.Id, out var prev))
                {
                    var elapsed = (now - prev.Timestamp).TotalSeconds;
                    if (elapsed >= 0.2)
                    {
                        dlSpeed = Math.Max(0, (stats.BytesReceived - prev.BytesRecv) / elapsed);
                        ulSpeed = Math.Max(0, (stats.BytesSent - prev.BytesSent) / elapsed);
                        _nicSpeedHistory[nic.Id] = (stats.BytesReceived, stats.BytesSent, now);
                    }
                }
                else
                {
                    _nicSpeedHistory[nic.Id] = (stats.BytesReceived, stats.BytesSent, now);
                }

                list.Add(new NetworkAdapterMetric
                {
                    Id = nic.Id,
                    Name = nic.Name,
                    Description = nic.Description,
                    InterfaceType = nic.NetworkInterfaceType.ToString(),
                    MacAddress = nic.GetPhysicalAddress().ToString(),
                    Status = nic.OperationalStatus.ToString(),
                    IsConnected = nic.OperationalStatus == OperationalStatus.Up,
                    Ipv4Address = ipv4,
                    Ipv6Address = ipv6,
                    Gateway = gateway,
                    DnsServers = dns,
                    BytesReceived = stats.BytesReceived,
                    BytesSent = stats.BytesSent,
                    DownloadSpeedBps = Math.Round(dlSpeed, 1),
                    UploadSpeedBps = Math.Round(ulSpeed, 1)
                });
            }
        }
        catch
        {
            // Fallback
        }

        return list;
    }

    private static BatteryInfo GetBatteryInfoInternal()
    {
        var info = new BatteryInfo();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT EstimatedChargeRemaining, BatteryStatus, EstimatedRunTime FROM Win32_Battery");
            foreach (var item in searcher.Get())
            {
                info.HasBattery = true;
                if (int.TryParse(item["EstimatedChargeRemaining"]?.ToString(), out var percent))
                    info.EstimatedChargeRemainingPercent = percent;

                if (int.TryParse(item["BatteryStatus"]?.ToString(), out var status))
                {
                    // 1 = Other, 2 = Unknown, 3 = Fully Charged, 4 = Low, 5 = Critical, 6 = Charging, 7 = Charging and High, etc.
                    info.IsCharging = status == 2 || status == 6 || status == 7 || status == 8;
                    info.PowerStatus = info.IsCharging ? "Charging" : "Discharging";
                }

                if (int.TryParse(item["EstimatedRunTime"]?.ToString(), out var runTimeMinutes) && runTimeMinutes > 0 && runTimeMinutes < 71582788)
                {
                    info.EstimatedRunTime = TimeSpan.FromMinutes(runTimeMinutes);
                }
                break;
            }
        }
        catch
        {
            info.HasBattery = false;
        }

        return info;
    }

    private static MotherboardInfo GetMotherboardInternal()
    {
        var mb = new MotherboardInfo();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard");
            foreach (var item in searcher.Get())
            {
                mb.Manufacturer = item["Manufacturer"]?.ToString()?.Trim() ?? string.Empty;
                mb.Product = item["Product"]?.ToString()?.Trim() ?? string.Empty;
                break;
            }

            using var biosSearcher = new ManagementObjectSearcher("SELECT SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS");
            foreach (var item in biosSearcher.Get())
            {
                mb.BiosVersion = item["SMBIOSBIOSVersion"]?.ToString()?.Trim() ?? string.Empty;
                mb.BiosDate = item["ReleaseDate"]?.ToString()?.Trim() ?? string.Empty;
                break;
            }
        }
        catch
        {
            // Fallback
        }

        return mb;
    }
}
