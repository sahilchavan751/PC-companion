using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using OmniControl.App.ViewModels;
using OmniControl.App.Views;
using OmniControl.Core.Models.Search;

namespace OmniControl.App;

public sealed partial class MainPage : Page
{
    public MainViewModel ViewModel { get; }

    public MainPage()
    {
        ViewModel = App.Services.GetRequiredService<MainViewModel>();
        InitializeComponent();

        ViewModel.NavigationRequested += OnNavigationRequested;

        // Navigate to default initial page
        ContentFrame.Navigate(typeof(OverviewPage));
        NavView.SelectedItem = NavView.MenuItems[0];

        Loaded += OnMainPageLoaded;
    }

    private async void OnMainPageLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        Loaded -= OnMainPageLoaded;
        try
        {
            ShimmerStoryboard?.Begin();
        }
        catch { }

        // Brief cinematic pause while initial telemetry loads
        await Task.Delay(1100);

        try
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    SplashFadeOutStoryboard.Completed += (s, ev) =>
                    {
                        try
                        {
                            SplashOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
                            ShimmerStoryboard?.Stop();
                        }
                        catch { }
                    };
                    SplashFadeOutStoryboard.Begin();
                }
                catch
                {
                    SplashOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
                }
            });
        }
        catch
        {
            SplashOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        }
    }

    private void NavView_PointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(NavView).Position;
        double threshold = NavView.IsPaneOpen ? NavView.OpenPaneLength : NavView.CompactPaneLength;
        if (pt.X <= threshold)
        {
            if (!NavView.IsPaneOpen)
            {
                NavView.IsPaneOpen = true;
            }
        }
        else
        {
            if (NavView.IsPaneOpen)
            {
                NavView.IsPaneOpen = false;
            }
        }
    }

    private void NavView_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (NavView.IsPaneOpen)
        {
            NavView.IsPaneOpen = false;
        }
    }

    private void OnNavViewSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            ContentFrame.Navigate(typeof(SettingsPage));
            ViewModel.CurrentTitle = "Settings";
            return;
        }

        if (args.SelectedItemContainer is NavigationViewItem item && item.Tag is string tag)
        {
            NavigateToTag(tag);
        }
    }

    private void OnNavigationRequested(string tag)
    {
        NavigateToTag(tag);

        // Select the matching menu item in NavView
        foreach (var menuItem in NavView.MenuItems)
        {
            if (menuItem is NavigationViewItem nvi && (string)nvi.Tag == tag)
            {
                NavView.SelectedItem = nvi;
                break;
            }
        }
    }

    private void NavigateToTag(string tag)
    {
        var (targetPage, title) = tag switch
        {
            "Overview" => (typeof(OverviewPage), "System Overview"),
            "Performance" => (typeof(PerformancePage), "Performance Control"),
            "Processes" => (typeof(ProcessesPage), "Process Manager"),
            "Startup" => (typeof(StartupPage), "Startup Manager"),
            "Environment" => (typeof(EnvironmentPage), "Environment Variables & PATH"),
            "Services" => (typeof(ServicesPage), "Windows Services"),
            "Hardware" => (typeof(HardwarePage), "Hardware & Sensors"),
            "AudioDisplay" => (typeof(AudioDisplayPage), "Audio & Display Control"),
            "Network" => (typeof(NetworkPage), "Network Manager"),
            "Storage" => (typeof(StoragePage), "Storage Manager"),
            "Security" => (typeof(SecurityPage), "Security Center"),
            "History" => (typeof(HistoryPage), "Audit History & Backups"),
            "Settings" => (typeof(SettingsPage), "Settings"),
            _ => (typeof(OverviewPage), "System Overview")
        };

        ContentFrame.Navigate(targetPage);
        ViewModel.CurrentTitle = title;
    }

    private async void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            await ViewModel.PerformSearchCommand.ExecuteAsync(sender.Text);
        }
    }

    private void OnSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is GlobalSearchResult result)
        {
            ViewModel.SelectSearchResultCommand.Execute(result);
        }
    }
}
