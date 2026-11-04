using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OmniControl.App.ViewModels;
using OmniControl.Core.Models.Hardware;

namespace OmniControl.App.Views;

public sealed partial class PerformancePage : Page
{
    public PerformanceViewModel ViewModel { get; }

    public PerformancePage()
    {
        ViewModel = App.Services.GetRequiredService<PerformanceViewModel>();
        InitializeComponent();
    }

    private async void OnSetActivePlanClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: PowerPlanInfo plan })
        {
            await ViewModel.SelectPowerPlanAsync(plan);
        }
    }
}
