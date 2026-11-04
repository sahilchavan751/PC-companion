using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using OmniControl.App.ViewModels;

namespace OmniControl.App.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        ViewModel = App.Services.GetRequiredService<SettingsViewModel>();
        InitializeComponent();
    }
}
