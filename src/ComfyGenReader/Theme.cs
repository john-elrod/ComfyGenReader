using System.Windows;
using System.Windows.Media;

namespace ComfyGenReader;

/// <summary>Switches the whole app between the light and dark Fluent themes, plus the colors the theme does not cover.</summary>
internal static class Theme
{
    public static void Apply(Application app, bool dark)
    {
#pragma warning disable WPF0001 // ThemeMode is still marked experimental in .NET 10
        app.ThemeMode = dark ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001
        // Find highlights sit behind Raw's text, so they must stay readable under the theme's text color.
        app.Resources["FindMatchBrush"] = Frozen(dark ? Color.FromRgb(0x5C, 0x4B, 0x00) : Colors.Yellow);
        app.Resources["FindCurrentMatchBrush"] = Frozen(dark ? Color.FromRgb(0xA3, 0x52, 0x00) : Colors.Orange);
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
