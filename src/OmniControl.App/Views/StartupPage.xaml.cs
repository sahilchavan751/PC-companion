using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OmniControl.App.ViewModels;
using OmniControl.Core.Models.Startup;

namespace OmniControl.App.Views;

public sealed partial class StartupPage : Page
{
    public StartupViewModel ViewModel { get; }

    public StartupPage()
    {
        ViewModel = App.Services.GetRequiredService<StartupViewModel>();
        InitializeComponent();
    }

    private async void OnStartupToggleChanged(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch ts && (ts.DataContext as StartupEntry ?? ts.Tag as StartupEntry) is { } entry)
        {
            if (entry.IsEnabled == ts.IsOn) return;
            await ViewModel.SetEntryStateAsync(entry, ts.IsOn);
            ts.IsOn = entry.IsEnabled;
        }
    }
}
