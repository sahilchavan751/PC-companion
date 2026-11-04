using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OmniControl.App.ViewModels;
using OmniControl.Core.Models.AudioDisplay;

namespace OmniControl.App.Views;

public sealed partial class AudioDisplayPage : Page
{
    public AudioDisplayViewModel ViewModel { get; }

    public AudioDisplayPage()
    {
        ViewModel = App.Services.GetRequiredService<AudioDisplayViewModel>();
        InitializeComponent();
    }

    private async void OnAppVolumeChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (sender is Slider { DataContext: AudioSessionItem session })
        {
            await ViewModel.UpdateSessionVolumeAsync((session, e.NewValue));
        }
    }

    private async void OnToggleAppMuteClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AudioSessionItem session })
        {
            await ViewModel.ToggleSessionMuteAsync(session);
        }
    }

    private void OnBrightnessSliderValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        ViewModel?.SetBrightnessDirect(e.NewValue);
    }
}
