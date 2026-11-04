using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using OmniControl.App.ViewModels;

namespace OmniControl.App.Views;

public sealed partial class SecurityPage : Page
{
    public SecurityViewModel ViewModel { get; }

    public SecurityPage()
    {
        ViewModel = App.Services.GetRequiredService<SecurityViewModel>();
        InitializeComponent();
    }
}
