using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;

namespace OmniControl.Core.Models.Processes;

public enum ProcessCategory
{
    Application,
    BackgroundProcess,
    WindowsProcess
}

public class ProcessItem : ObservableObject
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;

    private string? _iconPath;
    public string? IconPath
    {
        get => _iconPath;
        set
        {
            if (SetProperty(ref _iconPath, value))
            {
                OnPropertyChanged(nameof(HasIcon));
            }
        }
    }

    public bool HasIcon => !string.IsNullOrEmpty(IconPath);

    public string DisplayName => !string.IsNullOrWhiteSpace(Description) ? Description : Name;

    public string SubtitleText => !string.IsNullOrWhiteSpace(Description) && !string.Equals(Description, Name, StringComparison.OrdinalIgnoreCase)
        ? $"{Name}.exe"
        : (!string.IsNullOrWhiteSpace(Publisher) ? Publisher : $"{Name}.exe");

    public string CategoryCode => Category switch
    {
        ProcessCategory.Application => "APP",
        ProcessCategory.BackgroundProcess => "BKG",
        ProcessCategory.WindowsProcess => "WIN",
        _ => "SYS"
    };

    private double _cpuPercent;
    public double CpuPercent
    {
        get => _cpuPercent;
        set
        {
            if (SetProperty(ref _cpuPercent, value))
            {
                OnPropertyChanged(nameof(CpuFormatted));
                OnPropertyChanged(nameof(CpuColor));
            }
        }
    }

    public string CpuColor => CpuPercent switch
    {
        > 25.0 => "#FF3D57",
        > 10.0 => "#FFB300",
        > 1.0 => "#00D4FF",
        _ => "#888888"
    };

    private long _workingSetBytes;
    public long WorkingSetBytes
    {
        get => _workingSetBytes;
        set
        {
            if (SetProperty(ref _workingSetBytes, value))
            {
                OnPropertyChanged(nameof(MemoryFormatted));
            }
        }
    }

    public long PrivateMemoryBytes { get; set; }
    public ProcessPriorityClass Priority { get; set; } = ProcessPriorityClass.Normal;
    public int ThreadCount { get; set; }
    public int HandleCount { get; set; }
    public ProcessCategory Category { get; set; } = ProcessCategory.Application;

    private bool _isResponding = true;
    public bool IsResponding
    {
        get => _isResponding;
        set
        {
            if (SetProperty(ref _isResponding, value))
            {
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public bool IsCritical { get; set; }
    public string StatusText => IsResponding ? "Running" : "Not Responding";
    public DateTime? StartTime { get; set; }
    public TimeSpan? TotalProcessorTime { get; set; }
    public string MemoryFormatted => FormatBytes(WorkingSetBytes);
    public string CpuFormatted => $"{CpuPercent:F1}%";

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
}
