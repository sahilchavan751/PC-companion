using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using OmniControl.App.ViewModels;
using OmniControl.Core.Models.Hardware;
using OmniControl.Core.Models.Storage;

namespace OmniControl.App.Views;

public sealed partial class StoragePage : Page
{
    public StorageViewModel ViewModel { get; }

    public StoragePage()
    {
        ViewModel = App.Services.GetRequiredService<StorageViewModel>();
        InitializeComponent();
    }

    private void OnDriveCardClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && (fe.DataContext as DiskDriveInfo ?? fe.Tag as DiskDriveInfo) is { } drive)
        {
            ViewModel.SelectDrive(drive);
        }
    }

    private async void OnTreeItemDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && (fe.DataContext as StorageFolderItem ?? fe.Tag as StorageFolderItem) is { } item)
        {
            if (item.ItemType == StorageItemType.Folder)
            {
                await ViewModel.NavigateToFolderAsync(item);
            }
        }
    }

    private async void OnDrillIntoFolderClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && (fe.DataContext as StorageFolderItem ?? fe.Tag as StorageFolderItem) is { } item)
        {
            if (item.ItemType == StorageItemType.Folder)
            {
                await ViewModel.NavigateToFolderAsync(item);
            }
        }
    }

    private void OnOpenFileLocationClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe)
        {
            if (fe.DataContext is StorageFolderItem folderItem)
            {
                ViewModel.OpenFileLocation(folderItem.FullPath);
            }
            else if (fe.DataContext is LargeFileItem largeFile)
            {
                ViewModel.OpenFileLocation(largeFile.FilePath);
            }
            else if (fe.Tag is string path)
            {
                ViewModel.OpenFileLocation(path);
            }
        }
    }

    private async void OnQuickJumpClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string target)
        {
            await ViewModel.QuickJumpAsync(target);
        }
    }

    private void OnViewModeClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string mode)
        {
            ViewModel.SelectViewMode(mode);
        }
    }
}
