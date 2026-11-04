namespace OmniControl.Core.Models.Services;

public enum WindowsServiceState
{
    Running,
    Stopped,
    Paused,
    StartPending,
    StopPending,
    ContinuePending,
    PausePending,
    Unknown
}

public enum WindowsServiceStartMode
{
    Automatic,
    AutomaticDelayedStart,
    Manual,
    Disabled,
    Unknown
}

public class WindowsServiceItem : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public string ServiceName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    private WindowsServiceState _state = WindowsServiceState.Unknown;
    public WindowsServiceState State
    {
        get => _state;
        set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(StateBadgeColor));
            }
        }
    }

    public WindowsServiceStartMode StartMode { get; set; } = WindowsServiceStartMode.Unknown;
    public string BinaryPath { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public bool CanStop { get; set; }
    public bool CanPauseAndContinue { get; set; }
    public bool CanShutdown { get; set; }
    public bool IsEssentialService { get; set; }
    public List<string> DependentServices { get; set; } = new();
    public List<string> ServicesDependedOn { get; set; } = new();

    public string StateBadgeColor => State switch
    {
        WindowsServiceState.Running => "#107C41",
        WindowsServiceState.Stopped => "#797775",
        WindowsServiceState.Paused => "#D83B01",
        _ => "#0078D4"
    };
}
