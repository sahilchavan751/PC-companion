using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OmniControl.App.ViewModels;
using OmniControl.Core.Models.Services;

namespace OmniControl.App.Views;

public sealed partial class ServicesPage : Page
{
    public ServicesViewModel ViewModel { get; }

    public ServicesPage()
    {
        ViewModel = App.Services.GetRequiredService<ServicesViewModel>();
        InitializeComponent();
    }

    private async void OnStartServiceClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: WindowsServiceItem service })
        {
            await ViewModel.StartServiceAsync(service);
        }
    }

    private async void OnStopServiceClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: WindowsServiceItem service })
        {
            await ViewModel.StopServiceAsync(service);
        }
    }

    private async void OnRestartServiceClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: WindowsServiceItem service })
        {
            await ViewModel.RestartServiceAsync(service);
        }
    }
}
