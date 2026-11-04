using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OmniControl.App.ViewModels;
using OmniControl.Core.Models.Environment;

namespace OmniControl.App.Views;

public sealed partial class EnvironmentPage : Page
{
    public EnvironmentViewModel ViewModel { get; }

    public EnvironmentPage()
    {
        ViewModel = App.Services.GetRequiredService<EnvironmentViewModel>();
        InitializeComponent();
    }

    private async void OnSwitchToUserScopeClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.SwitchScopeAsync("User");
    }

    private async void OnSwitchToMachineScopeClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.SwitchScopeAsync("Machine");
    }

    private async void OnMovePathUpClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: PathItem item })
        {
            await ViewModel.MovePathUpAsync(item);
        }
    }

    private async void OnMovePathDownClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: PathItem item })
        {
            await ViewModel.MovePathDownAsync(item);
        }
    }

    private async void OnRemovePathClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: PathItem item })
        {
            await ViewModel.RemovePathItemAsync(item);
        }
    }

    private async void OnDeleteVariableClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: EnvVariableItem item })
        {
            await ViewModel.DeleteVariableAsync(item);
        }
    }
}
