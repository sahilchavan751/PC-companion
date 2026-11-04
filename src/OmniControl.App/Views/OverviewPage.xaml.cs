using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using OmniControl.App.ViewModels;

namespace OmniControl.App.Views;

public sealed partial class OverviewPage : Page
{
    public OverviewViewModel ViewModel { get; }

    public OverviewPage()
    {
        ViewModel = App.Services.GetRequiredService<OverviewViewModel>();
        InitializeComponent();
    }
}
