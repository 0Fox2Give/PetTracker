using System;
using System.Windows;
namespace PetTracker;

static class PlacementChecks
{
    public static void Run(Action<bool, string> check)
    {
        var window = new Window { Width = 340, Height = 220 };
        try
        {
            var settings = new AppSettings { Left = 2500, Top = 1300 };
            var desktop = new Rect(0, 0, 1920, 1080);
            OverlayGeometry.RestorePosition(window, settings, desktop);
            check(window.Left == 1580 && window.Top == 860, "Restoring a disconnected-monitor position puts the tracker within the desktop");
            window.Left = 100;
            window.Top = 100;
            OverlayGeometry.RestorePosition(window, settings, desktop);
            check(window.Left == 1580 && window.Top == 860 && settings.Left == 2500 && settings.Top == 1300, "Repeated status restoration stays reachable without rewriting the saved position");
            settings.Left = -1700;
            settings.Top = -900;
            desktop = new Rect(-1920, -1080, 3840, 2160);
            OverlayGeometry.RestorePosition(window, settings, desktop);
            check(window.Left == -1700 && window.Top == -900, "Legitimate negative monitor coordinates are preserved");
            settings.Left = 1800;
            settings.Top = 1000;
            window.Width = 510;
            window.Height = 1600;
            OverlayGeometry.RestorePosition(window, settings, new Rect(0, 0, 1920, 1080));
            check(window.Left == 1410 && window.Top == 0, "A resized or very tall tracker keeps its header on the desktop");
            settings.Left = null;
            settings.Top = null;
            window.Left = double.NaN;
            window.Top = double.NaN;
            OverlayGeometry.RestorePosition(window, settings, new Rect(0, 0, 1920, 1080));
            check(double.IsFinite(window.Left) && window.Left >= 0 && window.Top == 0, "Unset initial window coordinates resolve to a finite reachable position");
            window.Width = 340;
            window.Height = 220;
            var screens = new[] { new Rect(0, 0, 1920, 1040), new Rect(1920, -1080, 1920, 1040) };
            settings.Left = 2400;
            settings.Top = 400;
            OverlayGeometry.RestorePosition(window, settings, screens);
            check(window.Left == 2400 && window.Top == -260, "A saved position in a diagonal monitor gap moves to an actual work area");
            double recoveredLeft = window.Left, recoveredTop = window.Top;
            OverlayGeometry.RestorePosition(window, settings, screens);
            check(window.Left == recoveredLeft && window.Top == recoveredTop && settings.Left == 2400 && settings.Top == 400, "Repeated polling keeps gap recovery stable and preserves saved preferences");
            settings.Left = 2400;
            settings.Top = -800;
            OverlayGeometry.RestorePosition(window, settings, screens);
            check(window.Left == 2400 && window.Top == -800, "A reachable position on the elevated monitor is preserved");
            settings.Left = 100;
            settings.Top = 950;
            OverlayGeometry.RestorePosition(window, settings, screens);
            check(window.Top == 820, "The tracker stays above the monitor taskbar");
            var leftScreen = new[] { new Rect(-1280, -200, 1280, 984), new Rect(0, 0, 1536, 824) };
            settings.Left = -1000;
            settings.Top = -100;
            window.Width = 510;
            window.Height = 330;
            OverlayGeometry.RestorePosition(window, settings, leftScreen);
            check(window.Left == -1000 && window.Top == -100, "Converted monitor coordinates preserve scaled negative positions");
            window.Height = 1800;
            settings.Top = 600;
            OverlayGeometry.RestorePosition(window, settings, leftScreen);
            check(window.Top == -200, "An oversized tracker keeps its header on the selected screen");
            check(OverlayGeometry.WorkAreas(window).Length > 0, "Native monitor enumeration supplies usable work areas");
            check(!window.IsVisible, "Placement checks never display a sample overlay over the game");
        }
        finally
        {
            window.Close();
        }
    }
}
