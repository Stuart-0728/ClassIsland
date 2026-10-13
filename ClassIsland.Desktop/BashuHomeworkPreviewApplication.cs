using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using ClassIsland.Views;
using FluentAvalonia.Styling;

namespace ClassIsland.Desktop;

// Standalone preview deliberately bypasses AppEntry, hosted services and saved classroom bindings.
public sealed class BashuHomeworkPreviewApplication : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Light;
        Styles.Add(new FluentAvaloniaTheme { CustomAccentColor = Avalonia.Media.Color.Parse("#00897B") });
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new BashuHomeworkPreviewWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
