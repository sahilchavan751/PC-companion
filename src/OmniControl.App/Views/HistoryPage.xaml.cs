using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OmniControl.App.ViewModels;
using OmniControl.Core.Models.History;

namespace OmniControl.App.Views;

public sealed partial class HistoryPage : Page
{
    public HistoryViewModel ViewModel { get; }

    public HistoryPage()
    {
        ViewModel = App.Services.GetRequiredService<HistoryViewModel>();
        InitializeComponent();
    }

    private async void OnUndoClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ChangeHistoryRecord record })
        {
            await ViewModel.UndoAsync(record);
        }
    }
}
