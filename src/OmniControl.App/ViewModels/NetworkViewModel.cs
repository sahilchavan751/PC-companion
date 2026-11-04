using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.Core.Interfaces;
using OmniControl.Core.Models.Hardware;

namespace OmniControl.App.ViewModels;

public partial class NetworkViewModel : ObservableObject
{
    private readonly INetworkManagerService _networkService;
    private readonly MainViewModel _mainViewModel;

    [ObservableProperty]
    private string _pingTarget = "1.1.1.1";

    [ObservableProperty]
    private string _pingResultText = "Ready to test";

    [ObservableProperty]
    private string _dnsTarget = "microsoft.com";

    [ObservableProperty]
    private string _dnsResultText = "Ready to resolve";

    [ObservableProperty]
    private bool _isTesting;

    [ObservableProperty]
    private bool _isLoading;

    public ObservableCollection<NetworkAdapterMetric> Adapters { get; } = new();

    private readonly SynchronizationContext? _syncContext;
    private readonly Timer? _autoRefreshTimer;
    private bool _isDisposed;

    public NetworkViewModel(INetworkManagerService networkService, MainViewModel mainViewModel)
    {
        _networkService = networkService;
        _mainViewModel = mainViewModel;
        _syncContext = SynchronizationContext.Current;

        _ = RefreshAdaptersAsync();

        // 3-second live auto-refresh
        _autoRefreshTimer = new Timer(async _ =>
        {
            if (_isDisposed) return;
            try
            {
                var list = await _networkService.GetNetworkAdaptersAsync();
                _syncContext?.Post(__ =>
                {
                    if (_isDisposed) return;
                    if (list.Count != Adapters.Count)
                    {
                        Adapters.Clear();
                        foreach (var a in list) Adapters.Add(a);
                    }
                    else
                    {
                        for (int i = 0; i < list.Count; i++)
                        {
                            Adapters[i].BytesReceived = list[i].BytesReceived;
                            Adapters[i].BytesSent = list[i].BytesSent;
                            Adapters[i].Status = list[i].Status;
                        }
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
    public async Task RefreshAdaptersAsync()
    {
        IsLoading = true;
        try
        {
            var list = await _networkService.GetNetworkAdaptersAsync();
            Adapters.Clear();
            foreach (var a in list)
            {
                Adapters.Add(a);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task RunPingAsync()
    {
        IsTesting = true;
        PingResultText = "Pinging...";
        try
        {
            var res = await _networkService.RunPingTestAsync(PingTarget);
            PingResultText = res.Message;
        }
        finally
        {
            IsTesting = false;
        }
    }

    [RelayCommand]
    public async Task RunDnsLookupAsync()
    {
        IsTesting = true;
        DnsResultText = "Resolving...";
        try
        {
            var res = await _networkService.RunDnsLookupAsync(DnsTarget);
            DnsResultText = res.Message;
        }
        finally
        {
            IsTesting = false;
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _autoRefreshTimer?.Dispose();
    }
}
