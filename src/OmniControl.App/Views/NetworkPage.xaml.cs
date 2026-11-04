using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using OmniControl.App.ViewModels;

namespace OmniControl.App.Views;

public sealed partial class NetworkPage : Page
{
    public NetworkViewModel ViewModel { get; }

    public NetworkPage()
    {
        ViewModel = App.Services.GetRequiredService<NetworkViewModel>();
        InitializeComponent();
    }
}
