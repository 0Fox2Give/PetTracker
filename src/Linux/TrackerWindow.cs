using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace PetTracker.Linux;

public sealed class TrackerWindow : Window
{
    readonly ITrackerConnection capture;
    readonly Tracker tracker = new("", Catalog.Create());
    readonly MerchantDisplay merchant = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly StackPanel rows = new() { Spacing = 2 };
    readonly TextBlock status = new() { Text = "Connecting to capture…", TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    readonly CheckBox hideMax = new() { Content = "Hide maxed" };
    readonly Button recent;
    readonly Button nearby;
    WorkerView? view;
    FinderWindow? finder;
    Window? settings;
    TextBlock? help;
    bool polling, closing, allowClose, refreshing;
    string rendered = "";
    public TrackerWindow(ITrackerConnection capture)
    {
        this.capture = capture;
        Title = "Pet Tracker · Linux preview";
        Width = 380;
        Height = 380;
        MinWidth = 340;
        MinHeight = 240;
        Topmost = true;
        Background = Brush.Parse("#0B1118");
        var layout = new DockPanel { Margin = new Thickness(14) };
        var header = new StackPanel { Spacing = 10 };
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(Button("Find a pet", () =>
        {
            OpenFinder();
            return Task.CompletedTask;
        }));
        actions.Children.Add(Button("Settings", () =>
        {
            OpenSettings();
            return Task.CompletedTask;
        }));
        header.Children.Add(actions);
        var modes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        recent = Button("Recent", () => Patch(nameof(AppSettings.TrackerMode), "Recent"));
        nearby = Button("Nearby", () => Patch(nameof(AppSettings.TrackerMode), "Nearby"));
        modes.Children.Add(recent);
        modes.Children.Add(nearby);
        modes.Children.Add(hideMax);
        hideMax.IsCheckedChanged += async (_, _) =>
        {
            if (!refreshing)
                await Patch(nameof(AppSettings.HideMaxed), hideMax.IsChecked == true);
        };
        header.Children.Add(modes);
        header.Children.Add(status);
        layout.Children.Add(new ScrollViewer { Content = rows });
        Content = layout;
        timer.Tick += async (_, _) => await Poll();
        Opened += async (_, _) =>
        {
            timer.Start();
            await Poll();
        };
        Closing += async (_, e) =>
        {
            if (allowClose)
                return;
            e.Cancel = true;
            if (closing)
                return;
            closing = true;
            timer.Stop();
            try
            {
                await capture.Stop();
                allowClose = true;
                finder?.Close();
                settings?.Close();
                Close();
            }
            catch (Exception ex)
            {
                ShowError("Could not stop capture: " + ex.Message);
                closing = false;
                timer.Start();
            }
        };
    }

    public async Task Poll()
    {
        if (polling || closing)
            return;
        polling = true;
        try
        {
            Accept(await capture.Send(new("status")));
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            polling = false;
        }
    }

    public async Task Change(Command command)
    {
        try
        {
            Accept(await capture.Send(command));
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    Task Patch(string name, object value) => Change(new("patch", Patch: SettingsPatch.Fields((name, value))));

    void ShowError(string message)
    {
        status.Text = message;
        status.IsVisible = true;
        if (help != null)
            help.Text = message;
        // A lost worker connection cannot establish that a shop offer is current.
        merchant.Select(null, DateTimeOffset.UtcNow);
        rows.Children.Clear();
        rendered = "";
    }
    void Accept(WorkerView current)
    {
        view = current;
        tracker.ReadOnlyView(current);
        refreshing = true;
        hideMax.IsChecked = current.Settings.HideMaxed;
        refreshing = false;
        recent.FontWeight = current.Settings.TrackerMode == "Recent" ? FontWeight.Bold : FontWeight.Normal;
        nearby.FontWeight = current.Settings.TrackerMode == "Nearby" ? FontWeight.Bold : FontWeight.Normal;
        status.Text = current.Error ?? (current.Ready ? "" : current.Status + "\n" + current.Detail);
        status.IsVisible = !string.IsNullOrWhiteSpace(status.Text);
        if (help != null)
            help.Text = current.Status + "\n" + current.Detail + "\n" + current.Error;
        finder?.Accept(current);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var offers = merchant.Select(current, now);
        var activity = Activity.Select(tracker, current.Settings, now).ToArray();
        string empty = activity.Length == 0 && offers.Count == 0 ? Activity.EmptyText(tracker, current.Settings, now, current) : "";
        // Avoid rebuilding controls every second when the displayed values are unchanged.
        string key = JsonSerializer.Serialize(new { Merchant = offers.Select(o => new { o.Offer.Price, o.Updated, Pet = current.Profile.Pets[o.Offer.PetId] }), Activity = activity.Select(a => new { Pet = current.Profile.Pets[a.PetId!.Value], Notice = Activity.ObservationNotice(a, current.Settings, now) }), Empty = empty });
        if (key == rendered)
            return;
        rendered = key;
        rows.Children.Clear();
        if (offers.Count > 0)
        {
            rows.Children.Add(new TextBlock { Text = "Black Cloud Merchant", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 12, 0, 0) });
            rows.Children.Add(new TextBlock { Text = "Pets only from Chests or Black Cloud Merchants", FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightSlateGray });
            foreach (var offer in offers)
                rows.Children.Add(new PetRow(current.Profile.Pets[offer.Offer.PetId], offer.Updated ? null : offer.Offer.Price));
        }

        var merchantIds = offers.Select(o => o.Offer.PetId).ToHashSet();
        foreach (var item in activity.Where(a => !merchantIds.Contains(a.PetId!.Value)))
            rows.Children.Add(new PetRow(current.Profile.Pets[item.PetId!.Value], notice: Activity.ObservationNotice(item, current.Settings, now)));
        if (empty.Length > 0)
            rows.Children.Add(new TextBlock { Text = empty, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16), Foreground = Brushes.LightSlateGray });
    }

    void OpenFinder()
    {
        if (finder == null)
        {
            finder = new FinderWindow(Change);
            finder.Closed += (_, _) => finder = null;
            if (view != null)
                finder.Accept(view);
            finder.Show(this);
        }

        finder.Activate();
    }

    void OpenSettings()
    {
        if (settings != null)
        {
            settings.Activate();
            return;
        }

        var panel = new StackPanel { Spacing = 12, Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = "Tracking", FontSize = 22 });
        void Toggle(string label, bool initial, string property)
        {
            var option = new CheckBox { Content = label, IsChecked = initial };
            option.IsCheckedChanged += async (_, _) => await Patch(property, option.IsChecked == true);
            panel.Children.Add(option);
        }

        var preferences = view?.Settings ?? new();
        Toggle("Share collection across characters", preferences.ShareAllCharacters, nameof(AppSettings.ShareAllCharacters));
        Toggle("Merchant recommendations", preferences.BlackMarketAlerts, nameof(AppSettings.BlackMarketAlerts));
        Toggle("Clear inactive Recent rows", preferences.ExpireInactive, nameof(AppSettings.ExpireInactive));
        panel.Children.Add(new TextBlock { Text = "Pinned pet limit" });
        var limit = new ComboBox
        {
            ItemsSource = new[]
            {
                1,
                2,
                3,
                4,
                5
            },
            SelectedItem = preferences.PinLimit,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        limit.SelectionChanged += async (_, _) =>
        {
            if (limit.SelectedItem is int count)
                await Patch(nameof(AppSettings.PinLimit), count);
        };
        panel.Children.Add(limit);
        var topmost = new CheckBox { Content = "Keep tracker above other windows", IsChecked = Topmost };
        topmost.IsCheckedChanged += (_, _) => Topmost = topmost.IsChecked == true;
        panel.Children.Add(topmost);
        panel.Children.Add(new TextBlock { Text = "Connection", FontSize = 22, Margin = new Thickness(0, 10, 0, 0) });
        help = new TextBlock { Text = view?.Status ?? "Connecting…", TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(help);
        panel.Children.Add(new TextBlock { Text = "Start Pet Tracker before logging into your character. Collection sync happens at login. Live capture needs libpcap and permission for the capture worker; see docs/LINUX.md.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(Button("Run connection check", () => Change(new("activity-check"))));
        panel.Children.Add(new TextBlock { Text = "Linux preview · Update manually from a new Linux build. Windows update packages are not compatible.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightSlateGray });
        settings = new Window
        {
            Title = "Pet Tracker settings",
            Width = 480,
            Height = 650,
            Content = new ScrollViewer
            {
                Content = panel
            }
        };
        settings.Closed += (_, _) =>
        {
            settings = null;
            help = null;
        };
        settings.Show(this);
    }

    internal static Button Button(string label, Func<Task> action)
    {
        var button = new Button { Content = label };
        button.Click += async (_, _) => await action();
        return button;
    }
}
