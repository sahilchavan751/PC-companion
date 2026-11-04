using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace OmniControl.Infrastructure.WindowsInterop;

/// <summary>
/// Thread-safe icon extraction and disk-caching service for processes and applications.
/// </summary>
public static class AppIconExtractor
{
    private static readonly ConcurrentDictionary<string, string?> _iconCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly string IconCacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OmniControl", "IconCache");

    public static string? GetOrExtractIcon(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return null;

        // Strip quotes or leading/trailing whitespace
        filePath = filePath.Trim('\"', ' ');

        if (!File.Exists(filePath))
            return null;

        return _iconCache.GetOrAdd(filePath, path =>
        {
            try
            {
                Directory.CreateDirectory(IconCacheDirectory);
                var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(path.ToLowerInvariant()));
                var hash = Convert.ToHexString(hashBytes)[..16];
                var cachedPng = Path.Combine(IconCacheDirectory, $"{hash}.png");

                if (File.Exists(cachedPng))
                {
                    return cachedPng;
                }

                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon != null)
                {
                    using var bmp = icon.ToBitmap();
                    bmp.Save(cachedPng, System.Drawing.Imaging.ImageFormat.Png);
                    return cachedPng;
                }
            }
            catch
            {
                // Fallback gracefully on extraction error
            }

            return null;
        });
    }
}

