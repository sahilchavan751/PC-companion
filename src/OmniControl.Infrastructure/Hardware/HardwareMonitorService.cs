using LibreHardwareMonitor.Hardware;
using OmniControl.Core.Interfaces;
using OmniControl.Core.Models.Hardware;

namespace OmniControl.Infrastructure.Hardware;

public class HardwareMonitorService : IHardwareMonitorService
{
    private readonly Computer _computer;
    private readonly ISystemInfoService _systemInfoService;
    private readonly Timer _monitorTimer;
    private readonly List<SensorMetric> _latestSensors = new();
    private readonly object _lock = new();
    private bool _isOpen;
    private bool _isDisposed;

    public event EventHandler<HardwareUpdateEventArgs>? MetricsUpdated;

    public HardwareMonitorService(ISystemInfoService systemInfoService)
    {
        _systemInfoService = systemInfoService;

        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,
            IsStorageEnabled = true,
            IsNetworkEnabled = true,
            IsControllerEnabled = true
        };

        try
        {
            _computer.Open();
            _isOpen = true;
        }
        catch
        {
            _isOpen = false;
        }

        _monitorTimer = new Timer(OnTimerTick, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void StartMonitoring(int intervalMilliseconds = 2000)
    {
        _monitorTimer.Change(0, Math.Max(250, intervalMilliseconds));
    }

    public void StopMonitoring()
    {
        _monitorTimer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    public IReadOnlyList<SensorMetric> GetAllSensors()
    {
        lock (_lock)
        {
            return _latestSensors.ToList();
        }
    }

    private async void OnTimerTick(object? state)
    {
        if (_isDisposed) return;

        try
        {
            var cpu = await _systemInfoService.GetCpuInfoAsync();
            var ram = await _systemInfoService.GetRamInfoAsync();
            var disks = await _systemInfoService.GetDisksAsync();
            var nics = await _systemInfoService.GetNetworkAdaptersAsync();
            var gpus = await _systemInfoService.GetGpuInfoAsync();

            var sensors = new List<SensorMetric>();

            if (_isOpen)
            {
                foreach (var hardware in _computer.Hardware)
                {
                    try
                    {
                        hardware.Update();

                        foreach (var subHardware in hardware.SubHardware)
                        {
                            subHardware.Update();
                        }

                        foreach (var s in hardware.Sensors)
                        {
                            sensors.Add(new SensorMetric
                            {
                                Name = s.Name,
                                Category = s.SensorType.ToString(),
                                Value = s.Value.HasValue ? Math.Round((double)s.Value.Value, 1) : null,
                                Unit = GetUnit(s.SensorType),
                                HardwareName = hardware.Name
                            });

                            // Inject high-accuracy temperatures and load into CPU/GPU models
                            if (hardware.HardwareType == HardwareType.Cpu)
                            {
                                if (s.SensorType == SensorType.Temperature)
                                {
                                    if (s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (!cpu.TemperatureCelsius.HasValue && s.Value.HasValue)
                                        {
                                            cpu.TemperatureCelsius = Math.Round((double)s.Value.Value, 1);
                                        }
                                    }
                                }
                                else if (s.SensorType == SensorType.Load && s.Value.HasValue)
                                {
                                    if (s.Name.Contains("Total", StringComparison.OrdinalIgnoreCase) || s.Name == "CPU Total")
                                    {
                                        cpu.UtilizationPercent = Math.Round((double)s.Value.Value, 1);
                                    }
                                }
                            }
                            else if (hardware.HardwareType == HardwareType.GpuNvidia || hardware.HardwareType == HardwareType.GpuAmd || hardware.HardwareType == HardwareType.GpuIntel)
                            {
                                var targetGpu = gpus.FirstOrDefault(g =>
                                    g.Name.Contains(hardware.Name, StringComparison.OrdinalIgnoreCase) ||
                                    hardware.Name.Contains(g.Name, StringComparison.OrdinalIgnoreCase)) ?? (gpus.Count > 0 ? gpus[0] : null);

                                if (targetGpu != null && s.Value.HasValue)
                                {
                                    if (s.SensorType == SensorType.Temperature)
                                    {
                                        if (!targetGpu.TemperatureCelsius.HasValue || s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                                        {
                                            targetGpu.TemperatureCelsius = Math.Round((double)s.Value.Value, 1);
                                        }
                                    }
                                    else if (s.SensorType == SensorType.Load)
                                    {
                                        if (s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) ||
                                            s.Name.Contains("GPU", StringComparison.OrdinalIgnoreCase) ||
                                            s.Name.Contains("3D", StringComparison.OrdinalIgnoreCase) ||
                                            s.Name.Contains("D3D", StringComparison.OrdinalIgnoreCase))
                                        {
                                            targetGpu.UtilizationPercent = Math.Round((double)s.Value.Value, 1);
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Ignore individual hardware sensor update failure
                    }
                }
            }

            lock (_lock)
            {
                _latestSensors.Clear();
                _latestSensors.AddRange(sensors);
            }

            MetricsUpdated?.Invoke(this, new HardwareUpdateEventArgs
            {
                Cpu = cpu,
                Ram = ram,
                Disks = disks,
                NetworkAdapters = nics,
                Gpus = gpus,
                Sensors = sensors
            });
        }
        catch
        {
            // Safeguard against unhandled timer exceptions
        }
    }

    private static string GetUnit(SensorType type) => type switch
    {
        SensorType.Voltage => "V",
        SensorType.Clock => "MHz",
        SensorType.Temperature => "°C",
        SensorType.Load => "%",
        SensorType.Frequency => "Hz",
        SensorType.Fan => "RPM",
        SensorType.Flow => "L/h",
        SensorType.Control => "%",
        SensorType.Level => "%",
        SensorType.Power => "W",
        SensorType.Data => "GB",
        SensorType.SmallData => "MB",
        SensorType.Throughput => "B/s",
        _ => string.Empty
    };

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        StopMonitoring();
        _monitorTimer.Dispose();

        if (_isOpen)
        {
            try
            {
                _computer.Close();
            }
            catch
            {
                // Suppress on close
            }
        }
    }
}
