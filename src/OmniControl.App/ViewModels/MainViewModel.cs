using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.Core.AdminContract;
using OmniControl.Core.Interfaces;
using OmniControl.Core.Models.Search;

namespace OmniControl.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IAdminElevationService _elevationService;
    private readonly IGlobalSearchService _searchService;

    [ObservableProperty]
    private string _currentTitle = "System Overview";

    [ObservableProperty]
    private bool _isRunningAsAdmin;

    [ObservableProperty]
    private string _adminStatusText = "Standard User";

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isSearchOpen;

    [ObservableProperty]
    private string _statusMessage = "All systems operational";

    [ObservableProperty]
    private string _infoBarMessage = string.Empty;

    [ObservableProperty]
    private bool _isInfoBarOpen;

    [ObservableProperty]
    private string _infoBarSeverity = "Informational";

    public ObservableCollection<GlobalSearchResult> SearchResults { get; } = new();

    public event Action<string>? NavigationRequested;

    public MainViewModel(IAdminElevationService elevationService, IGlobalSearchService searchService)
    {
        _elevationService = elevationService;
        _searchService = searchService;

        RefreshAdminStatus();
    }

    public void RefreshAdminStatus()
    {
        IsRunningAsAdmin = _elevationService.IsAppRunningElevated;
        AdminStatusText = IsRunningAsAdmin ? "Administrator" : "Standard User (Elevate on Demand)";
    }

    [RelayCommand]
    private async Task RequestElevationAsync()
    {
        if (IsRunningAsAdmin)
        {
            ShowInfoBar("Application is already running with full Administrator privileges.", "Informational");
            return;
        }

        var success = await _elevationService.EnsureHelperElevatedAsync();
        if (success)
        {
            RefreshAdminStatus();
            ShowInfoBar("Elevated helper service initialized and ready for administrative operations.", "Success");
        }
        else
        {
            ShowInfoBar("Administrator prompt was dismissed. Privileged actions will request elevation when triggered.", "Warning");
        }
    }

    [RelayCommand]
    private async Task PerformSearchAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            SearchResults.Clear();
            IsSearchOpen = false;
            return;
        }

        var results = await _searchService.SearchAsync(query);
        SearchResults.Clear();
        foreach (var r in results)
        {
            SearchResults.Add(r);
        }

        IsSearchOpen = SearchResults.Count > 0;
    }

    [RelayCommand]
    private void SelectSearchResult(GlobalSearchResult result)
    {
        if (result == null) return;

        IsSearchOpen = false;
        SearchQuery = string.Empty;

        if (result.DirectAction != null)
        {
            result.DirectAction.Invoke();
        }
        else if (!string.IsNullOrEmpty(result.NavigationTargetTag))
        {
            NavigationRequested?.Invoke(result.NavigationTargetTag);
        }
    }

    public void ShowInfoBar(string message, string severity = "Informational")
    {
        InfoBarMessage = message;
        InfoBarSeverity = severity;
        IsInfoBarOpen = true;
    }

    [RelayCommand]
    private void CloseInfoBar()
    {
        IsInfoBarOpen = false;
    }
}
