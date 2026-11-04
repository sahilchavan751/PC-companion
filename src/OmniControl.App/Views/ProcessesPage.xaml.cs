using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OmniControl.App.ViewModels;
using OmniControl.Core.Models.Processes;

namespace OmniControl.App.Views;

public sealed partial class ProcessesPage : Page
{
    public ProcessesViewModel ViewModel { get; }

    public ProcessesPage()
    {
        ViewModel = App.Services.GetRequiredService<ProcessesViewModel>();
        InitializeComponent();
    }

    private void OnSortColumnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string col })
        {
            ViewModel.ToggleSort(col);
        }
    }

    private async void OnEndTaskItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ProcessItem process })
        {
            await ViewModel.EndTaskAsync(process);
        }
    }

    private async void OnRestartTaskItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ProcessItem process })
        {
            await ViewModel.RestartTaskAsync(process);
        }
    }

    private void OnOpenFileLocationClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ProcessItem process })
        {
            ViewModel.OpenFileLocation(process);
        }
    }

    private async void OnSetPriorityItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: string prioStr, DataContext: ProcessItem process })
        {
            if (Enum.TryParse<ProcessPriorityClass>(prioStr, out var priority))
            {
                await ViewModel.SetPriorityAsync((process, priority));
            }
        }
    }
}
