using System.Windows.Input;

using System.Windows.Interop;

using System.Runtime.InteropServices;

namespace PetTracker;

public static class NativeTheme
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    public static void DarkTitleBar(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle;
            int enabled = 1, caption = 0x0017100B, text = 0x00EFE6DF;
            DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int));
            DwmSetWindowAttribute(handle, 35, ref caption, sizeof(int));
            DwmSetWindowAttribute(handle, 36, ref text, sizeof(int));
        };
    }

    public static ResourceDictionary Resources() => new() { Source = new Uri("/PetTracker;component/NativeTheme.xaml", UriKind.Relative) };

    public static void Install(Window w)
    {
        w.Resources.MergedDictionaries.Add(Resources());
        w.Background = Ui.Dark;
        w.Foreground = Ui.Ink;
        w.FontFamily = new FontFamily("Segoe UI");
        w.UseLayoutRounding = true;
    }

    public static Brush Accent(string value) => new SolidColorBrush(value switch { "Blue" => Color.FromRgb(96, 177, 236), "Teal" => Color.FromRgb(83, 201, 181), _ => Color.FromRgb(217, 178, 76) });

}

public static class PetHudActivityPolicy
{

    public const double FadeSeconds = 2, CollapseSeconds = .18, RowHeight = 78;

    public static double Age(RecentPetActivity a, DateTimeOffset now) => Math.Max(0, (now - a.SeenUtc).TotalSeconds);

    public static IEnumerable<RecentPetActivity> Select(Tracker t, AppSettings s, DateTimeOffset now) => Activity.Select(t, s, now);

    public static (double Opacity, double Height) Appearance(RecentPetActivity a, AppSettings s, DateTimeOffset now)
    {
        if (a.IsNearby || s.IsPinned(a.PetId) || !s.ExpireInactive)
            return (1, RowHeight);
        double rem = s.InactiveSeconds - Age(a, now), c = Math.Clamp(1 + rem / .18, 0, 1);
        return (Math.Clamp(rem / 2, 0, 1), RowHeight * c * c * (3 - 2 * c));
    }

}

public static class OverlayGeometry
{
    public static Point Clamp(Point p, Rect r, double w, double h) => new(Math.Clamp(p.X, r.Left, Math.Max(r.Left, r.Right - w)), Math.Clamp(p.Y, r.Top, Math.Max(r.Top, r.Bottom - h)));
    // WPF window positions use device-independent coordinates. Convert each
    // physical monitor with this window's current transform, including negative
    // origins; a virtual-desktop bounding box includes unreachable gaps.
    public static Rect[] WorkAreas(Window window)
    {
        var source = PresentationSource.FromVisual(window);
        var dpi = VisualTreeHelper.GetDpi(window);
        var transform = source?.CompositionTarget?.TransformFromDevice ?? new Matrix(1 / dpi.DpiScaleX, 0, 0, 1 / dpi.DpiScaleY, 0, 0);
        return System.Windows.Forms.Screen.AllScreens.Select(screen =>
        {
            var r = screen.WorkingArea;
            return new Rect(transform.Transform(new Point(r.Left, r.Top)), transform.Transform(new Point(r.Right, r.Bottom)));
        }).ToArray();
    }
    public static Point ClampToScreens(Point p, IReadOnlyList<Rect> screens, double width, double height)
    {
        if (screens.Count == 0)
            return Clamp(p, SystemParameters.WorkArea, width, height);
        return screens.Select(screen => Clamp(p, screen, width, height))
         .OrderBy(candidate => (candidate - p).LengthSquared).First();
    }
    public static void RestorePosition(Window window, AppSettings settings, Rect desktop) => RestorePosition(window, settings, new[] { desktop });
    public static void RestorePosition(Window window, AppSettings settings, IReadOnlyList<Rect> screens)
    {
        var desktop = screens.Count > 0 ? screens[0] : SystemParameters.WorkArea;
        double left = settings.Left ?? window.Left, top = settings.Top ?? window.Top;
        if (!double.IsFinite(left))
            left = desktop.Left + 40;
        if (!double.IsFinite(top))
            top = desktop.Top + 80;
        double width = double.IsFinite(window.Width) ? window.Width : window.ActualWidth;
        double height = window.ActualHeight > 0 ? window.ActualHeight : double.IsFinite(window.Height) ? window.Height : 64 * settings.Scale;
        var point = ClampToScreens(new Point(left, top), screens, width, height);
        // Status polling must not restore an unreachable saved position after a
        // resize/display change. Retain the preference for when that screen returns.
        window.Left = point.X;
        window.Top = point.Y;
    }
}


public sealed class PetPortraits(string root)
{
    readonly Dictionary<int, ImageSource> cache = new();

    public ImageSource Get(int id)
    {
        if (cache.TryGetValue(id, out var result))
            return result;
        string path = Path.Combine(root, "portraits", id + ".png");
        if (!File.Exists(path))
            path = Path.Combine(AppContext.BaseDirectory, "portraits", id + ".png");
        if (File.Exists(path))
            try
            {
                var image = new System.Windows.Media.Imaging.BitmapImage();
                using var s = File.OpenRead(path);
                image.BeginInit();
                image.StreamSource = s;
                image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 160;
                image.EndInit();
                image.Freeze();
                return cache[id] = image;
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException) { }

        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(27, 40, 56)), null, new Rect(0, 0, 48, 48), 8, 8);
            var fill = new SolidColorBrush(Color.FromRgb(146, 170, 193));
            dc.DrawEllipse(fill, null, new Point(24, 30), 11, 8);
            foreach (var p in new[] { new Point(12, 19), new Point(20, 13), new Point(29, 13), new Point(37, 20) })
                dc.DrawEllipse(fill, null, p, 4, 5);
        }
        group.Freeze();
        var fallback = new DrawingImage(group);
        fallback.Freeze();
        return cache[id] = fallback;

    }

}

public static class WindowBehavior
{

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr Get(IntPtr h, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr Set(IntPtr h, int index, IntPtr value);

    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

    public static bool GameForeground(string processes)
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById((int)pid);
            return processes.Split(',').Any(n => n.Trim().Equals(p.ProcessName, StringComparison.OrdinalIgnoreCase));
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static void ClickThrough(Window w, bool enabled)
    {
        IntPtr h = new WindowInteropHelper(w).Handle;
        if (h == IntPtr.Zero)
            return;
        long old = Get(h, -20).ToInt64();
        Set(h, -20, new IntPtr(enabled ? old | 0x20 : old & ~0x20));
    }

}
