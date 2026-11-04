namespace OmniControl.Core.Models.AudioDisplay;

public enum AudioDeviceDirection
{
    Playback,
    Recording
}

public class AudioDeviceInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public AudioDeviceDirection Direction { get; set; }
    public bool IsDefault { get; set; }
    public bool IsMuted { get; set; }
    public float VolumeLevelPercent { get; set; } // 0 - 100
    public string State { get; set; } = "Active";
}

public partial class AudioSessionItem : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public int ProcessId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private float _volumePercent;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _isMuted;
}

public class DisplayMonitorInfo
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string FriendlyName { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public int ResolutionWidth { get; set; }
    public int ResolutionHeight { get; set; }
    public int RefreshRateHz { get; set; }
    public int BitsPerPixel { get; set; }
    public int BrightnessPercent { get; set; } // 0 - 100
    public bool IsBrightnessSupported { get; set; }
    public string ResolutionText => $"{ResolutionWidth} x {ResolutionHeight} @ {RefreshRateHz}Hz";
}
