using System.Diagnostics;

using System.Runtime.InteropServices;

using System.Text.Json;

using System.Windows.Interop;

using System.Windows.Media.Imaging;

namespace PetTracker;

public static class Program
{

    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint modifiers, uint key);

    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);

    [STAThread]
    public static void Main(string[] args)
    {
        Velopack.VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(NativeTheme.Resources());

        if (args.Contains("--render"))
        {
            RenderPreview(args);
            return;
        }

        if (args.Contains("--probe"))
        {
            int ix = Array.IndexOf(args, "--probe");
            string output = args[ix + 1];
            using var probe = new Host(Storage.Root(args), false);
            try
            {
                var result = Task.Run(async () =>
                {
                    var initial = await probe.Send(new("status"));
                    var created = await probe.Send(new("create", Name: "FriendTest", Realm: "Test server"));
                    var again = await probe.Send(new("status"));
                    await probe.Send(new("exit"));
                    return new
                    {
                        initial.Status,
                        initial.Devices,
                        Ready = again.Ready,
                        Profiles = again.Profiles.Count,
                        ReconnectPreserved = created.Selected == again.Selected
                    };
                }).GetAwaiter().GetResult();
                Storage.Save(output, result);
            }
            catch (Exception ex)
            {
                Storage.Save(output, new
                {
                    Error = ex.Message
                });
            }
            return;
        }

        using var instance = new AppInstanceGate();
        if (!instance.IsPrimary || LegacyCopyRunning())
        {
            MessageBox.Show("Pet Tracker is already running. Use its paw icon in the Windows tray. To switch copies, choose Quit Pet Tracker there first.", "Pet Tracker", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string root = Storage.Root(args);
        using var mutex = new Mutex(true, "Local\\" + Storage.Pipe(root) + "-app", out bool first);
        if (!first)
            return;

        using var host = OpenHost(root, args.Contains("--activity-check"));
        if (host == null)
            return;
        StartupRegistration.RemoveLegacyEntry();
        host.StartHidden = args.Contains("--minimized");
        using var tray = new System.Windows.Forms.NotifyIcon { Text = "Pet Tracker", Visible = true, Icon = new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory, "PetTracker.ico")) };
        var menu = new System.Windows.Forms.ContextMenuStrip();
        tray.ContextMenuStrip = menu;

        menu.Items.Add("Show / hide tracker", null, (s, e) => host.SetPetOverlayVisible(!(host.Overlay?.IsVisible == true)));
        menu.Items.Add("Find a pet…", null, (s, e) => host.OpenPetFinder());
        menu.Items.Add("Settings", null, (s, e) => host.OpenPetOverlaySettings());
        menu.Items.Add("Unlock tracker", null, async (s, e) => await host.UnlockPetOverlay());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Help", null, (s, e) => host.OpenConnectionHelp());
        bool quitting = false;
        menu.Items.Add("Quit Pet Tracker", null, async (s, e) =>
        {
            if (quitting)
                return;
            quitting = true;
            try
            {
                await host.StopWorkerForQuit();
                tray.Visible = false;
                app.Shutdown();
            }
            catch (Exception error)
            {
                Ui.Error(error);
            }
            finally
            {
                quitting = false;
            }
        });
        host.Updates.Available += version => tray.ShowBalloonTip(5000, "Pet Tracker update available", "Version " + version + " is available. Open Settings → Updates to download it.", System.Windows.Forms.ToolTipIcon.Info);
        host.Updates.Ready += version => tray.ShowBalloonTip(5000, "Pet Tracker update ready", "Version " + version + " is ready. Open Settings → Updates when you want to restart.", System.Windows.Forms.ToolTipIcon.Info);
        tray.DoubleClick += (s, e) => host.OpenPetOverlaySettings();

        using var messages = new HwndSource(new HwndSourceParameters("Pet Tracker controls") { Width = 0, Height = 0, WindowStyle = 0 });
        messages.AddHook((IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled) =>
        {
            if (msg == 0x312)
            {
                host.SetPetOverlayVisible(!(host.Overlay?.IsVisible == true));
                handled = true;
            }
            return IntPtr.Zero;
        });
        int current = 0;

        host.Changed += () =>
        {
            int requested = host.PetOverlayPreferences.Hotkey;
            if (current != requested)
            {
                UnregisterHotKey(messages.Handle, 1);
                current = requested;
                host.SetHotkeyError("");
                if (current != 0 && !RegisterHotKey(messages.Handle, 1, 0x4003, (uint)current))
                {
                    host.SetHotkeyError("Shortcut unavailable. Choose another combination.");
                }
            }
        };

        host.TestNotification += () => tray.ShowBalloonTip(4000, "Pet Tracker", "Preview: your pet reached Rank 2.", System.Windows.Forms.ToolTipIcon.Info);

        host.Notification += text =>
        {
            if (host.PetOverlayPreferences.Toasts)
                tray.ShowBalloonTip(4000, "Pet Tracker", text, System.Windows.Forms.ToolTipIcon.Info);
        };

        app.DispatcherUnhandledException += (s, e) =>
        {
            e.Handled = true;
            Ui.Error(e.Exception);
        };
        app.Exit += (s, e) =>
        {
            UnregisterHotKey(messages.Handle, 1);
            tray.Visible = false;
        };
        app.Startup += async (s, e) => await host.InitializeLaunch(args.Contains("--minimized"));
        app.Run();

    }

    static Host? OpenHost(string root, bool activityCheck)
    {
        try
        {
            return new Host(root) { ActivityCheckOnStart = activityCheck };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show("Pet Tracker couldn’t open its saved data. Close any program using the files, check folder access, then try again.", "Pet Tracker", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
    }
    static bool LegacyCopyRunning()
    {
        using var current = Process.GetCurrentProcess();
        foreach (var process in Process.GetProcessesByName("PetTracker"))
        {
            using (process)
                try
                {
                    if (process.Id == current.Id || process.SessionId != current.SessionId)
                        continue;
                    string? file = process.MainModule?.FileName;
                    if (file != null && FileVersionInfo.GetVersionInfo(file).ProductName == "Pet Tracker")
                        return true;
                }
                catch (System.ComponentModel.Win32Exception) { }
                catch (InvalidOperationException) { }
        }
        return false;
    }

    static void RenderPreview(string[] args)
    {
        int index = Array.IndexOf(args, "--render");
        string folder = Path.GetFullPath(args[index + 1]);
        Directory.CreateDirectory(folder);
        using var host = new Host(Path.Combine(folder, "fixture"), false);
        var profile = new Profile { CharacterName = "Ranger", Pets = Catalog.Create() };
        foreach (var p in profile.Pets.Values)
        {
            p.Level = 0;
            p.Souls = 0;
            p.Goal = 5;
            p.CounterVerified = true;
        }

        int[] ids = { 1050, 1060, 1007, 1219, 1141 };
        int[] values = { 72, 5, 3, 58, 75 };
        for (int i = 0; i < ids.Length; i++)
        {
            var p = profile.Pets[ids[i]];
            p.Level = i == 4 ? 3 : 2;
            p.Souls = i == 4 ? null : values[i];
            p.Goal = i == 4 ? null : 75;
            p.LevelVerified = true;
        }

        var now = DateTimeOffset.UtcNow;
        var recent = ids.Select((id, i) => new RecentPetActivity(id, null, "", now.AddSeconds(-i))).ToList();
        var view = new WorkerView(3, profile, true, "Live", "Receiving soul updates from your character.", "Ranger", new() { new() { AccountName = "My account", CharacterName = "Ranger", RealmLabel = "Siel", ProfileKey = "1234567890abcdef12345678" } }, "1234567890abcdef12345678", recent, null, now, 1, 1000, 0, 0, new()
        {
            ExpireInactive = false
        });
        host.Preview(view);
        Save(host.Overlay!, Path.Combine(folder, "tracker.png"), 340, 410);
        var settings = new SettingsWindow(host);
        Save(settings, Path.Combine(folder, "settings.png"), 780, 700);
        foreach (var section in new[] { "Tracking", "Overlay", "Account", "Updates", "Help" })
        {
            settings.ShowSection(section);
            Save(settings, Path.Combine(folder, "settings-" + section.Replace(" & ", "-").ToLowerInvariant() + ".png"), 780, 700);
        }
        settings.Close();

    }

    public static void Save(Window window, string path, double width, double height)
    {
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var dc = background.RenderOpen())
            dc.DrawRectangle(Ui.Dark, null, new Rect(0, 0, width, height));
        bitmap.Render(background);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }

}
