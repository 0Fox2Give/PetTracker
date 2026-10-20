using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace PetTracker.Linux;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        string root = Storage.Root(args);
        using var gate = new Mutex(true, Storage.Pipe(root) + "-linux-ui", out bool first);
        if (!first)
            return;
        App.DataRoot = root;
        AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
    }
}

public sealed class App : Application
{
    public static string DataRoot { get; set; } = "";

    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new TrackerWindow(new CaptureClient(DataRoot));
        base.OnFrameworkInitializationCompleted();
    }
}
