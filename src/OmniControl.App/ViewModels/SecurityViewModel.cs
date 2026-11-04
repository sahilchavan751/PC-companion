using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.Core.Interfaces;
using OmniControl.Core.Models.Security;

namespace OmniControl.App.ViewModels;

public partial class SecurityViewModel : ObservableObject
{
    private readonly ISecurityCenterService _securityService;

    [ObservableProperty]
    private string _antivirusName = "Windows Defender";

    [ObservableProperty]
    private bool _antivirusActive = true;

    [ObservableProperty]
    private bool _firewallActive = true;

    [ObservableProperty]
    private string _firewallText = "Active";

    [ObservableProperty]
    private bool _uacActive = true;

    [ObservableProperty]
    private string _uacText = "Protected Mode";

    [ObservableProperty]
    private bool _secureBootActive;

    [ObservableProperty]
    private bool _isLoading;

    public ObservableCollection<string> Recommendations { get; } = new();

    private readonly SynchronizationContext? _syncContext;
    private readonly Timer? _autoRefreshTimer;
    private bool _isDisposed;

    public SecurityViewModel(ISecurityCenterService securityService)
    {
        _securityService = securityService;
        _syncContext = SynchronizationContext.Current;

        _ = RefreshSecurityStatusAsync();

        // 3-second live auto-refresh
        _autoRefreshTimer = new Timer(async _ =>
        {
            if (_isDisposed) return;
            try
            {
                var status = await _securityService.GetSecurityStatusAsync();
                _syncContext?.Post(__ =>
                {
                    if (_isDisposed) return;
                    AntivirusName = status.AntivirusProductName;
                    AntivirusActive = status.AntivirusEnabled;
                    FirewallActive = status.FirewallEnabled;
                    FirewallText = status.FirewallStatusText;
                    UacActive = status.UacEnabled;
                    UacText = status.UacLevelText;
                    SecureBootActive = status.SecureBootEnabled;
                }, null);
            }
            catch
            {
                // Suppress background poll errors
            }
        }, null, 2000, 2000);
    }

    [RelayCommand]
    public async Task RefreshSecurityStatusAsync()
    {
        IsLoading = true;
        try
        {
            var status = await _securityService.GetSecurityStatusAsync();
            AntivirusName = status.AntivirusProductName;
            AntivirusActive = status.AntivirusEnabled;
            FirewallActive = status.FirewallEnabled;
            FirewallText = status.FirewallStatusText;
            UacActive = status.UacEnabled;
            UacText = status.UacLevelText;
            SecureBootActive = status.SecureBootEnabled;

            Recommendations.Clear();
            foreach (var r in status.SecurityRecommendations)
            {
                Recommendations.Add(r);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _autoRefreshTimer?.Dispose();
    }
}
