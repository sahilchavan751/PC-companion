using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.Core.Interfaces;
using OmniControl.Core.Models.History;

namespace OmniControl.App.ViewModels;

public partial class HistoryViewModel : ObservableObject
{
    private readonly IOperationHistoryService _historyService;
    private readonly MainViewModel _mainViewModel;
    private readonly SynchronizationContext? _syncContext;
    private readonly PeriodicTimer _liveTimer = new(TimeSpan.FromSeconds(3));
    private readonly CancellationTokenSource _cts = new();

    [ObservableProperty]
    private string _snapshotName = "Manual Checkpoint";

    [ObservableProperty]
    private bool _isLoading;

    public ObservableCollection<ChangeHistoryRecord> Records { get; } = new();
    public ObservableCollection<BackupSnapshot> Snapshots { get; } = new();

    public HistoryViewModel(IOperationHistoryService historyService, MainViewModel mainViewModel)
    {
        _historyService = historyService;
        _mainViewModel = mainViewModel;
        _syncContext = SynchronizationContext.Current;

        _ = RefreshDataAsync();
        _ = RunLiveMonitoringLoopAsync(_cts.Token);
    }

    private async Task RunLiveMonitoringLoopAsync(CancellationToken token)
    {
        try
        {
            while (await _liveTimer.WaitForNextTickAsync(token))
            {
                var history = await _historyService.GetHistoryAsync(100);
                var snaps = await _historyService.GetSnapshotsAsync();
                _syncContext?.Post(_ =>
                {
                    if (history.Count != Records.Count)
                    {
                        Records.Clear();
                        foreach (var h in history) Records.Add(h);
                    }
                    if (snaps.Count != Snapshots.Count)
                    {
                        Snapshots.Clear();
                        foreach (var s in snaps) Snapshots.Add(s);
                    }
                }, null);
            }
        }
        catch (OperationCanceledException) { }
    }

    [RelayCommand]
    public async Task RefreshDataAsync()
    {
        IsLoading = true;
        try
        {
            var history = await _historyService.GetHistoryAsync(100);
            Records.Clear();
            foreach (var h in history) Records.Add(h);

            var snaps = await _historyService.GetSnapshotsAsync();
            Snapshots.Clear();
            foreach (var s in snaps) Snapshots.Add(s);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task UndoAsync(ChangeHistoryRecord record)
    {
        if (record == null) return;

        var res = await _historyService.UndoOperationAsync(record.Id);
        if (res.IsSuccess)
        {
            _mainViewModel.ShowInfoBar(res.Message, "Success");
            await RefreshDataAsync();
        }
        else
        {
            _mainViewModel.ShowInfoBar(res.Message, "Error");
        }
    }

    [RelayCommand]
    public async Task CreateCheckpointAsync()
    {
        var name = string.IsNullOrWhiteSpace(SnapshotName) ? $"Snapshot_{DateTime.Now:yyyyMMdd_HHmm}" : SnapshotName.Trim();
        var res = await _historyService.CreateBackupSnapshotAsync(name, "FullSystem", "{}");
        if (res.IsSuccess)
        {
            _mainViewModel.ShowInfoBar(res.Message, "Success");
            await RefreshDataAsync();
        }
        else
        {
            _mainViewModel.ShowInfoBar(res.Message, "Error");
        }
    }
}
