using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.Core.Interfaces;
using OmniControl.Core.Models.AudioDisplay;

namespace OmniControl.App.ViewModels;

public partial class AudioDisplayViewModel : ObservableObject
{
    private readonly IAudioControlService _audioService;
    private readonly IDisplayControlService _displayService;
    private readonly MainViewModel _mainViewModel;

    [ObservableProperty]
    private double _masterVolume = 50.0;

    [ObservableProperty]
    private bool _isMasterMuted;

    public string MasterMuteGlyph => IsMasterMuted ? "\uE74F" : "\uE767";
    public string MasterVolumeText => $"{(int)Math.Round(MasterVolume)}%";
    public string DisplayBrightnessText => $"{(int)Math.Round(DisplayBrightness)}%";

    partial void OnIsMasterMutedChanged(bool value)
    {
        OnPropertyChanged(nameof(MasterMuteGlyph));
    }

    partial void OnMasterVolumeChanged(double value)
    {
        OnPropertyChanged(nameof(MasterVolumeText));
        if (_isUpdatingFromService) return;
        _ = _audioService.SetMasterVolumeAsync((float)value);
    }

    [ObservableProperty]
    private double _displayBrightness = 100.0;

    private CancellationTokenSource? _brightnessDebounceCts;

    partial void OnDisplayBrightnessChanged(double value)
    {
        OnPropertyChanged(nameof(DisplayBrightnessText));
        if (_isUpdatingFromService) return;
        ScheduleBrightnessUpdate((int)Math.Round(value));
    }

    public void ScheduleBrightnessUpdate(int value)
    {
        _brightnessDebounceCts?.Cancel();
        _brightnessDebounceCts = new CancellationTokenSource();
        var token = _brightnessDebounceCts.Token;

        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(50, token);
                if (token.IsCancellationRequested) return;

                var devId = Monitors.Count > 0 ? Monitors[0].DeviceId : string.Empty;
                await _displayService.SetBrightnessAsync(devId, value, token);
            }
            catch (OperationCanceledException) { }
            catch { }
        }, token);
    }

    public void SetBrightnessDirect(double value)
    {
        if (Math.Abs(DisplayBrightness - value) > 0.5)
        {
            DisplayBrightness = value;
        }
        else
        {
            ScheduleBrightnessUpdate((int)Math.Round(value));
        }
    }

    [ObservableProperty]
    private bool _isBrightnessSupported = true;

    [ObservableProperty]
    private string _brightnessStatusText = "Screen brightness controls active";

    [ObservableProperty]
    private bool _isLoading;

    private bool _isUpdatingFromService;
    private readonly Timer? _autoRefreshTimer;
    private readonly SynchronizationContext? _syncContext;
    private bool _isDisposed;

    public ObservableCollection<AudioDeviceInfo> OutputDevices { get; } = new();
    public ObservableCollection<AudioDeviceInfo> InputDevices { get; } = new();
    public ObservableCollection<AudioSessionItem> AudioSessions { get; } = new();
    public ObservableCollection<DisplayMonitorInfo> Monitors { get; } = new();

    public AudioDisplayViewModel(
        IAudioControlService audioService,
        IDisplayControlService displayService,
        MainViewModel mainViewModel)
    {
        _audioService = audioService;
        _displayService = displayService;
        _mainViewModel = mainViewModel;
        _syncContext = SynchronizationContext.Current;

        _ = LoadAudioAndDisplayAsync();

        // 2-second periodic live refresh
        _autoRefreshTimer = new Timer(async _ =>
        {
            if (_isDisposed) return;
            try
            {
                var sysVol = await _audioService.GetMasterVolumeAsync();
                var sysMuted = await _audioService.GetMasterMuteAsync();
                var sessions = await _audioService.GetAudioSessionsAsync();

                _syncContext?.Post(__ =>
                {
                    if (_isDisposed) return;
                    _isUpdatingFromService = true;
                    try
                    {
                        if (Math.Abs(MasterVolume - sysVol) > 1.0)
                        {
                            MasterVolume = sysVol;
                        }
                        IsMasterMuted = sysMuted;

                        // Sync audio sessions
                        if (sessions.Count != AudioSessions.Count)
                        {
                            AudioSessions.Clear();
                            foreach (var s in sessions) AudioSessions.Add(s);
                        }
                        else
                        {
                            for (int i = 0; i < sessions.Count; i++)
                            {
                                AudioSessions[i].DisplayName = sessions[i].DisplayName;
                                AudioSessions[i].VolumePercent = sessions[i].VolumePercent;
                                AudioSessions[i].IsMuted = sessions[i].IsMuted;
                            }
                        }
                    }
                    finally
                    {
                        _isUpdatingFromService = false;
                    }
                }, null);
            }
            catch
            {
                // Suppress background poll errors
            }
        }, null, 2000, 2000);
    }

    [RelayCommand]
    public async Task LoadAudioAndDisplayAsync()
    {
        IsLoading = true;
        _isUpdatingFromService = true;
        try
        {
            MasterVolume = await _audioService.GetMasterVolumeAsync();
            IsMasterMuted = await _audioService.GetMasterMuteAsync();

            var devices = await _audioService.GetAudioDevicesAsync();
            OutputDevices.Clear();
            InputDevices.Clear();

            foreach (var d in devices)
            {
                if (d.Direction == AudioDeviceDirection.Playback)
                    OutputDevices.Add(d);
                else
                    InputDevices.Add(d);
            }

            var sessions = await _audioService.GetAudioSessionsAsync();
            AudioSessions.Clear();
            foreach (var s in sessions)
            {
                AudioSessions.Add(s);
            }

            var monitors = await _displayService.GetMonitorsAsync();
            Monitors.Clear();
            foreach (var m in monitors)
            {
                Monitors.Add(m);
            }

            if (monitors.Count > 0)
            {
                DisplayBrightness = monitors[0].BrightnessPercent;
                IsBrightnessSupported = monitors[0].IsBrightnessSupported;
                BrightnessStatusText = IsBrightnessSupported
                    ? "Hardware DDC/CI or screen brightness control active"
                    : "Software brightness control active";
            }
        }
        finally
        {
            _isUpdatingFromService = false;
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task ToggleMasterMuteAsync()
    {
        IsMasterMuted = !IsMasterMuted;
        var res = await _audioService.SetMasterMuteAsync(IsMasterMuted);
        if (res.IsSuccess)
        {
            _mainViewModel.ShowInfoBar(res.Message, "Success");
        }
    }

    [RelayCommand]
    public async Task UpdateSessionVolumeAsync((AudioSessionItem Session, double Volume) param)
    {
        param.Session.VolumePercent = (float)param.Volume;
        await _audioService.SetSessionVolumeAsync(param.Session.SessionId, param.Session.ProcessId, (float)param.Volume);
    }

    [RelayCommand]
    public async Task ToggleSessionMuteAsync(AudioSessionItem session)
    {
        if (session == null) return;
        session.IsMuted = !session.IsMuted;
        await _audioService.SetSessionMuteAsync(session.SessionId, session.ProcessId, session.IsMuted);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _autoRefreshTimer?.Dispose();
    }
}
