using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace OmniControl.App.Converters;

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool b)
        {
            return b ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value is Visibility v && v == Visibility.Visible;
    }
}

public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool b)
        {
            return b ? Visibility.Collapsed : Visibility.Visible;
        }
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value is Visibility v && v != Visibility.Visible;
    }
}

public class FormatBytesConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        long bytes = 0;
        if (value is long l) bytes = l;
        else if (value is ulong ul) bytes = (long)ul;
        else if (value is int i) bytes = i;

        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1 && counter < suffixes.Length - 1)
        {
            number /= 1024;
            counter++;
        }
        var formatted = $"{number:n1} {suffixes[counter]}";
        return parameter is string prefix ? $"{prefix}{formatted}" : formatted;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public class FilePathToImageSourceConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            var uri = new Uri(path);
            return new BitmapImage(uri);
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public class NullToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value != null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public class StateToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var text = value?.ToString()?.ToLowerInvariant() ?? string.Empty;
        if (text.Contains("running") || text.Contains("active") || text.Contains("true") || text.Contains("enabled"))
        {
            return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 34, 197, 94)); // Green
        }
        if (text.Contains("warning") || text.Contains("paused") || text.Contains("delayed"))
        {
            return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 245, 158, 11)); // Amber
        }
        if (text.Contains("stopped") || text.Contains("disabled") || text.Contains("false"))
        {
            return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 148, 163, 184)); // Gray
        }
        if (text.Contains("error") || text.Contains("fail"))
        {
            return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 239, 68, 68)); // Red
        }

        return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 180, 216)); // Cyan
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}
