using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using OmniControl.App.ViewModels;

namespace OmniControl.App.Views;

public sealed partial class HardwarePage : Page
{
    public HardwareViewModel ViewModel { get; }

    public HardwarePage()
    {
        ViewModel = App.Services.GetRequiredService<HardwareViewModel>();
        InitializeComponent();
    }
}
