using System.Windows.Media;

namespace Core.Views;

internal static class ThemePicker
{
    internal static ImageSource? Logo(Uri uri)
    {
        try
        {
            var image = new System.Windows.Media.Imaging.BitmapImage();
            image.BeginInit();
            image.UriSource = uri;
            image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is System.IO.IOException or NotSupportedException or UnauthorizedAccessException or ArgumentException)
        {
            App.LogError(ex);
            return null;
        }
    }
}
