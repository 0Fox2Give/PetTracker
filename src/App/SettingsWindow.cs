using System.Text.Json;
using System.Windows.Threading;
using PetTracker.Updates;
namespace PetTracker;

public sealed class SettingsWindow : Window
{
    readonly Host host;
    readonly TextBlock state = Ui.Text("", 13, Ui.Muted), saved = Ui.Text("Changes save automatically", 12, Ui.Muted), connection = Ui.Text("", 13, Ui.Muted), diagnostics = Ui.Text("", 11, Ui.Muted);
    readonly Grid pageHost = new();
    readonly Dictionary<string, ScrollViewer> pages = new();
    readonly Dictionary<string, Button> navigation = new();
    readonly Dictionary<string, JsonElement> pending = new();
    readonly List<Action<AppSettings>> bindings = new();
    readonly DispatcherTimer delay = new() { Interval = TimeSpan.FromMilliseconds(250) };
    readonly TextBlock syncHelp = Ui.Text("", 12, Ui.Muted), merchantStatus = Ui.Text("", 12, Ui.Muted);
    readonly TextBlock updateStatus = Ui.Text("", 13, Ui.Muted);
    readonly TextBlock releaseNotes = Ui.Text("", 12, Ui.Muted);
    Expander? notesPanel;
    readonly ProgressBar updateProgress = new() { Minimum = 0, Maximum = 100, Height = 4, Margin = new Thickness(0, 12, 0, 12) };
    Button? checkUpdates, downloadUpdate, installUpdate;
    StackPanel? updatePreferences;
    bool lastSaveSucceeded = true;
    readonly Button linkCharacter;
    Button? reset, npcap, connectionHelp;
    readonly StackPanel recentOptions = new(), nearbyOptions = new();
    readonly TextBlock nearbyHint = Ui.Text("Nearby lists recent mob observations, not a measured radius. Older sightings show when they were last seen.", 12, Ui.Muted), hotkeyStatus = Ui.Text("", 12, Ui.Muted);
    readonly Button discard;
    Dictionary<string, JsonElement>? inFlight;
    bool loading, saving, closeRequested, allowClose;
    string selectedPage = "Tracking";
    public SettingsWindow(Host host)
    {
        this.host = host;
        Title = "Pet Tracker — Settings";
        Width = 820;
        Height = 740;
        MinWidth = 720;
        MinHeight = 560;
        NativeTheme.Install(this);
        NativeTheme.DarkTitleBar(this);
        Resources[SystemParameters.VerticalScrollBarWidthKey] = 10.0;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new Grid { Margin = new Thickness(24) };
        Content = root;
        root.RowDefinitions.Add(new()
        {
            Height = GridLength.Auto
        });
        root.RowDefinitions.Add(new());
        root.RowDefinitions.Add(new()
        {
            Height = GridLength.Auto
        });
        root.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(150)
        });
        root.ColumnDefinitions.Add(new());
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 22) };
        header.Children.Add(Ui.Text("Settings", 25));
        state.Margin = new Thickness(0, 8, 0, 0);
        state.TextWrapping = TextWrapping.Wrap;
        header.Children.Add(state);
        syncHelp.TextWrapping = TextWrapping.Wrap;
        syncHelp.Margin = new Thickness(0, 8, 0, 0);
        linkCharacter = Ui.Button("Link character", async () =>
        {
            var view = host.View;
            var target = view?.Profiles.FirstOrDefault(p => p.ProfileKey == view.Selected);
            if (view?.DetectedCharacter is not { } name || target == null)
                return;
            if (view.Profiles.Count != 1)
            {
                host.OpenAccounts();
                return;
            }
            linkCharacter!.IsEnabled = false;
            try
            {
                if (!host.PreviewOnly)
                    await host.Change(new("link", Key: target.ProfileKey, Name: name));
            }
            finally
            {
                RefreshStatus();
            }
        });
        linkCharacter.Visibility = Visibility.Collapsed;
        linkCharacter.HorizontalAlignment = HorizontalAlignment.Left;
        linkCharacter.Margin = new Thickness(0, 10, 0, 0);
        header.Children.Add(linkCharacter);
        connectionHelp = Ui.Button("Connection help", ShowConnection);
        connectionHelp.HorizontalAlignment = HorizontalAlignment.Right;
        connectionHelp.VerticalAlignment = VerticalAlignment.Top;
        connectionHelp.Height = 32;
        connectionHelp.Margin = new Thickness(0);
        Grid.SetColumn(connectionHelp, 1);
        root.Children.Add(connectionHelp);
        Grid.SetColumnSpan(header, 2);
        root.Children.Insert(0, header);
        var nav = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        Grid.SetRow(nav, 1);
        root.Children.Add(nav);
        Grid.SetRow(pageHost, 1);
        Grid.SetColumn(pageHost, 1);
        root.Children.Add(pageHost);
        var footer = new DockPanel { Margin = new Thickness(0, 18, 0, 0) };
        var done = Ui.Button("Done", Close);
        DockPanel.SetDock(done, Dock.Right);
        footer.Children.Add(done);
        discard = Ui.Button("Discard changes", async () =>
        {
            if (saving)
                return;
            saving = true;
            discard!.IsEnabled = false;
            delay.Stop();
            try
            {
                pending.Clear();
                lastSaveSucceeded = true;
                closeRequested = false;
                discard.Visibility = Visibility.Collapsed;
                saved.Text = "Unsaved changes discarded";
            }
            finally
            {
                saving = false;
                discard.IsEnabled = true;
                SyncControls();
            }
        });
        discard.Visibility = Visibility.Collapsed;
        DockPanel.SetDock(discard, Dock.Right);
        footer.Children.Add(discard);
        reset = Ui.Button("Reset this section", () =>
        {
            foreach (var f in SettingsSections.Defaults(selectedPage))
                pending[f.Key] = f.Value;
            SyncControls();
            saved.Text = "Saving…";
            delay.Stop();
            delay.Start();
        });
        reset.ToolTip = "Reset preferences on this page. Keep pet progress, accounts and pinned goals.";
        DockPanel.SetDock(reset, Dock.Right);
        footer.Children.Add(reset);
        saved.TextWrapping = TextWrapping.Wrap;
        saved.VerticalAlignment = VerticalAlignment.Center;
        saved.Margin = new Thickness(0, 0, 8, 0);
        footer.Children.Add(saved);
        Grid.SetRow(footer, 2);
        Grid.SetColumnSpan(footer, 2);
        root.Children.Add(footer);
        StackPanel Page(string name)
        {
            var panel = new StackPanel { Margin = new Thickness(12, 0, 8, 0) };
            var heading = Ui.Text(name, 20);
            heading.FontWeight = FontWeights.SemiBold;
            heading.Margin = new Thickness(0, 0, 0, 16);
            panel.Children.Add(heading);
            var scroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Visibility = Visibility.Collapsed };
            pages[name] = scroll;
            pageHost.Children.Add(scroll);
            var button = Ui.Button(name, () => Select(name));
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Padding = new Thickness(12, 10, 12, 10);
            button.Margin = new Thickness(0, 0, 0, 8);
            navigation[name] = button;
            nav.Children.Add(button);
            return panel;
        }
        var tracker = Page("Tracking");
        var pets = Group(tracker, "Pet list");
        Choice(pets, "Tracker mode", new[] { "Recent", "Nearby" }, host.PetOverlayPreferences.TrackerMode, v => Queue(nameof(AppSettings.TrackerMode), v));
        Choice(pets, "Maximum shown pets", new[] { 3, 5, 8, 10 }, host.PetOverlayPreferences.RowLimit, v => Queue(nameof(AppSettings.RowLimit), v));
        var pins = Choice(pets, "Maximum pinned pets", new[] { 1, 2, 3, 4, 5 }, host.PetOverlayPreferences.PinLimit, v => Queue(nameof(AppSettings.PinLimit), v));
        pins.ToolTip = "Pin pets in Find a pet or right-click a tracker row. Pins keep their order. Lowering this limit keeps existing pins; the row limit grows when needed to leave room for activity.";
        Check(pets, "Hide maxed pets", nameof(AppSettings.HideMaxed));
        var merchant = Group(tracker, "Black Cloud Merchant");
        Check(merchant, "Show merchant shopping list in overlay", nameof(AppSettings.BlackMarketAlerts));
        Help(merchant, "Pets only from Chests or Black Cloud Merchants.");
        merchantStatus.TextWrapping = TextWrapping.Wrap;
        merchant.Children.Add(merchantStatus);
        var timing = new StackPanel();
        Choice(timing, "Sort", new[] { "Unfinished first", "Recent only", "Closest to next rank" }, host.PetOverlayPreferences.Sort, v => Queue(nameof(AppSettings.Sort), v)).ToolTip = "Unfinished first keeps Nearby in name order. Recent only puts the latest activity first. Pins always stay at the top.";
        Choice(recentOptions, "Hide inactive pets after", new[] { "Never", "15 seconds", "20 seconds", "30 seconds", "60 seconds", "120 seconds" }, Expiry(host.PetOverlayPreferences), v =>
        {
            Queue(nameof(AppSettings.ExpireInactive), v != "Never");
            if (v != "Never")
                Queue(nameof(AppSettings.InactiveSeconds), int.Parse(v.Split(' ')[0]));
        });
        timing.Children.Add(recentOptions);
        nearbyHint.TextWrapping = TextWrapping.Wrap;
        nearbyHint.Margin = new Thickness(0, 6, 0, 8);
        nearbyOptions.Children.Add(nearbyHint);
        Choice(nearbyOptions, "Hide unrefreshed mobs after (seconds)", new[] { 15, 30, 60, 120, 300 }, host.PetOverlayPreferences.NearbyStaleSeconds, v => Queue(nameof(AppSettings.NearbyStaleSeconds), v)).ToolTip = "Shorter times clear old sightings sooner but can hide stationary mobs. Pinned pets remain visible.";
        timing.Children.Add(nearbyOptions);
        Expand(tracker, "Sorting and timing", timing);

        var overlay = Page("Overlay");
        var display = Group(overlay, "Size and visibility");
        Slider(display, "Scale", nameof(AppSettings.Scale), .75, 1.5);
        Slider(display, "Background opacity", nameof(AppSettings.Opacity), .4, 1);
        Check(display, "Only show over Aion 2", nameof(AppSettings.GameOnly));
        var interaction = Group(overlay, "Position and interaction");
        Choice(interaction, "Interaction", new[] { "Move & interact", "Fixed position", "Pass clicks to game" }, Interaction(host.PetOverlayPreferences), v =>
        {
            Queue(nameof(AppSettings.Locked), v != "Move & interact");
            Queue(nameof(AppSettings.ClickThrough), v == "Pass clicks to game");
        }).ToolTip = "If clicks pass through the overlay, use the Windows tray paw icon to unlock it.";
        Actions(interaction, Ui.Button("Reposition tracker", async () =>
        {
            Queue(nameof(AppSettings.Left), SystemParameters.WorkArea.Left + 40);
            Queue(nameof(AppSettings.Top), SystemParameters.WorkArea.Top + 80);
            Queue(nameof(AppSettings.Locked), false);
            Queue(nameof(AppSettings.ClickThrough), false);
            if (await FinishPending())
                host.SetPetOverlayVisible(true);
        }));
        var moreOverlay = new StackPanel();
        Check(moreOverlay, "Hide when empty", nameof(AppSettings.HideWhenEmpty));
        Check(moreOverlay, "Reduce motion", nameof(AppSettings.ReducedMotion));
        Choice(moreOverlay, "Tracker shortcut", new[] { "Disabled", "Ctrl + Alt + P", "Ctrl + Alt + T", "Ctrl + Alt + O" }, Hotkey(host.PetOverlayPreferences.Hotkey), v => Queue(nameof(AppSettings.Hotkey), v switch { "Ctrl + Alt + P" => 0x50, "Ctrl + Alt + T" => 0x54, "Ctrl + Alt + O" => 0x4f, _ => 0 }));
        moreOverlay.Children.Add(hotkeyStatus);
        Help(moreOverlay, "The Windows tray paw icon can always show or unlock the overlay.");
        Expand(overlay, "More overlay options", moreOverlay);
        var notifications = new StackPanel();
        Check(notifications, "Desktop notification", nameof(AppSettings.Toasts));
        Check(notifications, "Play a sound", nameof(AppSettings.Sounds));
        Expand(overlay, "Rank notifications", notifications);

        var account = Page("Account");
        var collection = Group(account, "Collection");
        Check(collection, "Share progress across all characters", nameof(AppSettings.ShareAllCharacters));
        Help(collection, "For characters on the same game account. Turn this off before playing a separate account.");
        Actions(collection, Ui.Button("Manage accounts", host.OpenAccounts));
        syncHelp.Margin = new Thickness(0, 10, 0, 0);
        collection.Children.Add(syncHelp);
        var updatesPage = Page("Updates");
        var updates = Group(updatesPage, "App updates");
        Help(updates, "Pet Tracker " + Version() + " · Updates install when you choose to restart.");
        updateStatus.TextWrapping = TextWrapping.Wrap;
        updates.Children.Add(updateStatus);
        updates.Children.Add(updateProgress);
        releaseNotes.TextWrapping = TextWrapping.Wrap;
        notesPanel = new Expander { Header = "What's new", Content = new ScrollViewer { Content = releaseNotes, MaxHeight = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, Margin = new Thickness(0, 8, 0, 8) };
        updates.Children.Add(notesPanel);
        updatePreferences = new StackPanel();
        updates.Children.Add(updatePreferences);
        Check(updatePreferences, "Check for updates automatically", nameof(AppSettings.CheckUpdatesAutomatically));
        Check(updatePreferences, "Download updates automatically", nameof(AppSettings.DownloadUpdatesAutomatically));
        var channels = new StackPanel();
        Choice(channels, "Release channel", new[] { "Stable", "Beta" }, host.PetOverlayPreferences.UpdateChannel == "win-beta" ? "Beta" : "Stable", v => Queue(nameof(AppSettings.UpdateChannel), v == "Beta" ? "win-beta" : "win-stable"));
        Help(channels, "Stable is recommended. Changing channels never installs an older version.");
        Expand(updatePreferences, "Advanced update options", channels);
        checkUpdates = Ui.Button("Check for updates", async () =>
        {
            if (await FinishPending())
                await host.Updates.Check();
        });
        updates.Children.Add(checkUpdates);
        downloadUpdate = Ui.Button("Download update", async () =>
        {
            if (await FinishPending())
                await host.Updates.Download();
        });
        updates.Children.Add(downloadUpdate);
        installUpdate = Ui.Button("Restart to update", async () =>
        {
            if (!await FinishPending())
                return;
            IsEnabled = false;
            try
            {
                await host.Updates.Install();
            }
            finally
            {
                IsEnabled = true;
            }
        });
        updates.Children.Add(installUpdate);
        host.Updates.Session.Changed += RefreshUpdates;
        var helpPage = Page("Help");
        var help = Group(helpPage, "Connection");
        connection.TextWrapping = TextWrapping.Wrap;
        help.Children.Add(connection);
        Actions(help, Ui.Button("Refresh connection status", async () =>
        {
            if (await FinishPending())
            {
                bool ok = host.PreviewOnly || await host.Change(new("status"), false);
                RefreshStatus();
                saved.Text = ok ? "Connection status refreshed" : "Could not refresh connection status. Try again or save a support report.";
            }
        }));
        npcap = Ui.Button("Download Npcap", () => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://npcap.com/#download") { UseShellExecute = true }));
        Actions(help, npcap);
        var reports = new StackPanel();
        Help(reports, "Reports exclude account names, chat, packet samples and file paths. Nothing is uploaded automatically.");
        Actions(reports, Ui.Button("Copy support report", () =>
        {
            if (host.View is { } v)
            {
                Clipboard.SetText(SupportReport.Create(v, Version()));
                saved.Text = "Support report copied";
            }
        }), Ui.Button("Save support report…", SaveReport));
        Expand(helpPage, "Support report", reports);
        var advanced = new StackPanel();
        Section(advanced, "Capture recovery");
        Help(advanced, "Restart only when troubleshooting. Fresh collection totals may be needed afterward.");
        Actions(advanced, Ui.Button("Restart capture", async () =>
        {
            if (await FinishPending() && !host.PreviewOnly)
                await host.Change(new("restart"));
        }));
        var network = new StackPanel();
        var adapter = new ComboBox { DisplayMemberPath = "Label" };
        SettingRow(network, "Network adapter", adapter);
        void FillAdapters(IReadOnlyList<CaptureAdapterOption> available)
        {
            var id = (adapter.SelectedItem as CaptureAdapterOption)?.Id ?? host.PetOverlayPreferences.Adapter ?? "";
            var choices = new List<CaptureAdapterOption> { new("", "Automatic (recommended)") };
            choices.AddRange(available.DistinctBy(a => a.Id));
            if (id.Length > 0 && !choices.Any(a => a.Id == id))
                choices.Add(new(id, "Saved adapter (not detected)"));
            adapter.ItemsSource = choices;
            adapter.SelectedItem = choices.First(a => a.Id == id);
        }
        FillAdapters(Array.Empty<CaptureAdapterOption>());
        Actions(network, Ui.Button("Refresh adapters", async () =>
        {
            if (!host.PreviewOnly && await host.Change(new("adapters")))
                FillAdapters(host.View?.Adapters ?? Array.Empty<CaptureAdapterOption>());
        }));
        var processes = new TextBox { Text = host.PetOverlayPreferences.GameProcesses };
        SettingRow(network, "Game process names", processes);
        Actions(network, Ui.Button("Use automatic", () =>
        {
            adapter.SelectedIndex = 0;
            processes.Text = new AppSettings().GameProcesses;
        }), Ui.Button("Apply connection options", async () =>
        {
            if (!await FinishPending() || host.PreviewOnly)
                return;
            if (adapter.SelectedItem is not CaptureAdapterOption selected)
                return;
            if (await host.Change(new("patch", Patch: SettingsPatch.Fields((nameof(AppSettings.GameProcesses), processes.Text), (nameof(AppSettings.Adapter), selected.Id.Length == 0 ? null : selected.Id)))))
                await host.Change(new("restart"));
        }));
        Help(network, "Automatic uses available capture adapters. Apply restarts capture; these fields do not autosave.");
        Expand(advanced, "Connection overrides", network);
        Section(advanced, "Diagnostics");
        Actions(advanced, Ui.Button("Record activity check (90 seconds)", async () =>
        {
            if (!host.PreviewOnly && await host.Change(new("activity-check")))
                saved.Text = "Activity check started";
        }));
        Help(advanced, "Keeps a bounded local sample of combat and pet messages. Excludes chat and login records.");
        diagnostics.TextWrapping = TextWrapping.Wrap;
        advanced.Children.Add(diagnostics);
        Expand(helpPage, "Advanced troubleshooting", advanced);
        delay.Tick += async (s, e) =>
        {
            delay.Stop();
            await Flush();
        };
        Closing += (s, e) =>
        {
            if (!allowClose && (pending.Count > 0 || saving))
            {
                e.Cancel = true;
                closeRequested = true;
                if (!saving)
                    _ = Flush();
            }
        };
        Closed += (s, e) =>
        {
            delay.Stop();
            host.Updates.Session.Changed -= RefreshUpdates;
        };
        Select("Tracking");
        SyncControls();
        RefreshStatus();
    }
    async Task<bool> FinishPending()
    {
        while (saving)
            await Task.Delay(25);
        delay.Stop();
        await Flush();
        return lastSaveSucceeded;
    }
    void RefreshUpdates()
    {
        var update = host.Updates.Session.State;
        bool enabled = update.Phase != UpdatePhase.Unavailable, same = host.Updates.SameChannel;
        updateStatus.Text = !same && host.Updates.Session.Channel != null && !update.Busy ? "Channel changed. Check for updates on the selected channel." : update.Message;
        updateProgress.Visibility = update.Phase == UpdatePhase.Downloading ? Visibility.Visible : Visibility.Collapsed;
        updateProgress.Value = update.Progress;
        string notes = host.Updates.Session.ReleaseNotes;
        releaseNotes.Text = notes.Length > 6000 ? notes[..6000] + "…" : notes;
        if (notesPanel != null)
            notesPanel.Visibility = same && notes.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (updatePreferences != null)
            updatePreferences.IsEnabled = enabled && !update.Busy;
        if (checkUpdates != null)
            checkUpdates.IsEnabled = enabled && !update.Busy;
        if (downloadUpdate != null)
            downloadUpdate.Visibility = same && update.Phase == UpdatePhase.Available ? Visibility.Visible : Visibility.Collapsed;
        if (installUpdate != null)
            installUpdate.Visibility = same && update.Phase == UpdatePhase.Ready ? Visibility.Visible : Visibility.Collapsed;
    }
    static string Version() => typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3) ?? "Unknown";
    static string Hotkey(int key) => key switch { 0x50 => "Ctrl + Alt + P", 0x54 => "Ctrl + Alt + T", 0x4f => "Ctrl + Alt + O", _ => "Disabled" };
    static string Interaction(AppSettings s) => s.ClickThrough ? "Pass clicks to game" : s.Locked ? "Fixed position" : "Move & interact";
    static void Help(Panel p, string text)
    {
        p.Children.Add(new TextBlock { Text = text, Foreground = Ui.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 8) });
    }
    static string Expiry(AppSettings s) => s.ExpireInactive ? s.InactiveSeconds + " seconds" : "Never";
    static StackPanel Group(Panel parent, string title)
    {
        var content = new StackPanel();
        var label = Ui.Text(title, 13);
        label.FontWeight = FontWeights.SemiBold;
        label.Margin = new Thickness(0, 0, 0, 8);
        content.Children.Add(label);
        parent.Children.Add(new Border { Child = content, Background = new SolidColorBrush(Color.FromRgb(16, 23, 32)), BorderBrush = new SolidColorBrush(Color.FromRgb(39, 51, 66)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 12) });
        return content;
    }
    static void Section(Panel p, string text)
    {
        var label = Ui.Text(text, 13);
        label.FontWeight = FontWeights.SemiBold;
        label.Margin = new Thickness(0, 14, 0, 6);
        p.Children.Add(label);
    }
    static void Actions(Panel p, params Button[] buttons)
    {
        var row = new WrapPanel { Margin = new Thickness(0, 10, 0, 4) };
        foreach (var b in buttons)
        {
            b.Margin = new Thickness(0, 0, 8, 6);
            row.Children.Add(b);
        }
        p.Children.Add(row);
    }
    static void Expand(Panel p, string title, Panel content)
    {
        content.Margin = new Thickness(0, 8, 0, 8);
        p.Children.Add(new Expander { Header = title, Content = content, Margin = new Thickness(0, 12, 0, 4) });
    }
    static void SettingRow(Panel p, string label, FrameworkElement control)
    {
        var grid = new Grid { Margin = new Thickness(0, 7, 0, 7) };
        grid.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        grid.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(190)
        });
        var text = Ui.Text(label, 12, Ui.Muted);
        text.VerticalAlignment = VerticalAlignment.Center;
        text.Margin = new Thickness(0, 0, 12, 0);
        grid.Children.Add(text);
        control.Margin = new Thickness(0);
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        p.Children.Add(grid);
    }
    void SaveReport()
    {
        if (host.View is not { } v)
            return;
        var dialog = new Microsoft.Win32.SaveFileDialog { Title = "Save support report", FileName = "PetTracker-support.json", Filter = "Support report (*.json)|*.json", DefaultExt = ".json" };
        if (dialog.ShowDialog(this) == true)
            try
            {
                File.WriteAllText(dialog.FileName, SupportReport.Create(v, Version()));
                saved.Text = "Support report saved";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Ui.Error(ex, this);
            }
    }
    public static string ConnectionAssessment(WorkerView? v, string? error)
    {
        if (v == null)
            return "Capture component unavailable. " + (error ?? "Try restarting Pet Tracker from its shortcut.");
        if (v.Devices == 0)
            return "Capture is unavailable. Live tracking requires Npcap, installed separately. Download it below if missing, then restart Pet Tracker. If it is already installed, check Windows capture permissions and save a support report.";
        if (!v.Ready || v.Status != "Live")
            return v.Status + "\n" + v.Detail;
        if (v.Health?.LastPayload is { } last && DateTimeOffset.UtcNow - last > TimeSpan.FromSeconds(15))
            return "Connected, but no recent game traffic. Check that Aion 2 is running and connected.";
        if (v.Health?.LastPayload == null)
            return "Character connected. Traffic timing is not available yet.";
        return "Connected · game traffic is arriving.\n" + (v.Status == "Live" ? "Pet tracking is ready." : v.Detail);
    }
    void SyncControls()
    {
        loading = true;
        try
        {
            var options = host.PetOverlayPreferences;
            if (inFlight != null)
                options = SettingsPatch.Apply(options, inFlight);
            options = SettingsPatch.Apply(options, pending);
            foreach (var binding in bindings)
                binding(options);
            recentOptions.Visibility = options.TrackerMode == "Recent" ? Visibility.Visible : Visibility.Collapsed;
            nearbyOptions.Visibility = options.TrackerMode == "Nearby" ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            loading = false;
        }
    }
    void Queue(string name, object? value)
    {
        if (loading)
            return;
        pending[name] = JsonSerializer.SerializeToElement(value);
        SyncControls();
        saved.Text = "Saving…";
        delay.Stop();
        delay.Start();
    }
    void Check(Panel p, string label, string field)
    {
        var property = typeof(AppSettings).GetProperty(field)!;
        var checkbox = new CheckBox { Content = label, IsChecked = (bool)property.GetValue(host.PetOverlayPreferences)!, Margin = new Thickness(0, 10, 0, 4) };
        p.Children.Add(checkbox);
        checkbox.Checked += (s, e) => Queue(field, true);
        checkbox.Unchecked += (s, e) => Queue(field, false);
        bindings.Add(options => checkbox.IsChecked = (bool)property.GetValue(options)!);
    }
    ComboBox Choice<T>(Panel p, string label, T[] choices, T selected, Action<T> change)
    {
        var combo = new ComboBox { ItemsSource = choices, SelectedItem = selected };
        System.Windows.Automation.AutomationProperties.SetAutomationId(combo, label);
        SettingRow(p, label, combo);
        combo.SelectionChanged += (s, e) =>
        {
            if (!loading && combo.SelectedItem is T value)
                change(value);
        };
        bindings.Add(options => combo.SelectedItem = label switch { "Tracker mode" => options.TrackerMode, "Hide unrefreshed mobs after (seconds)" => options.NearbyStaleSeconds, "Maximum shown pets" => options.RowLimit, "Maximum pinned pets" => options.PinLimit, "Sort" => options.Sort, "Hide inactive pets after" => Expiry(options), "Tracker shortcut" => Hotkey(options.Hotkey), "Release channel" => options.UpdateChannel == "win-beta" ? "Beta" : "Stable", "Interaction" => Interaction(options), _ => selected });
        return combo;
    }
    void Slider(Panel p, string label, string field, double min, double max)
    {
        var property = typeof(AppSettings).GetProperty(field)!;
        var caption = Ui.Text("", 12, Ui.Muted);
        caption.Width = 46;
        caption.TextAlignment = TextAlignment.Right;
        var group = new DockPanel();
        DockPanel.SetDock(caption, Dock.Right);
        group.Children.Add(caption);
        var slider = new Slider { Minimum = min, Maximum = max, Value = (double)property.GetValue(host.PetOverlayPreferences)!, TickFrequency = .05, IsSnapToTickEnabled = true };
        group.Children.Add(slider);
        SettingRow(p, label, group);
        caption.Text = (slider.Value * 100).ToString("0") + "%";
        slider.ValueChanged += (s, e) =>
        {
            caption.Text = (slider.Value * 100).ToString("0") + "%";
            Queue(field, slider.Value);
        };
        bindings.Add(options => slider.Value = (double)property.GetValue(options)!);
    }
    async Task Flush()
    {
        if (saving)
            return;
        saving = true;
        discard.IsEnabled = false;
        try
        {
            while (pending.Count > 0)
            {
                var changes = new Dictionary<string, JsonElement>(pending);
                pending.Clear();
                inFlight = changes;
                bool success = await host.Change(new("patch", Patch: changes), false);
                lastSaveSucceeded = success;
                inFlight = null;
                if (!success)
                {
                    // Preserve failed edits, but never replace a newer edit made during this request.
                    foreach (var change in changes)
                        pending.TryAdd(change.Key, change.Value);
                    delay.Stop();
                    closeRequested = false;
                    saved.Text = "Not saved. Press Done to retry.";
                    discard.Visibility = Visibility.Visible;
                    break;
                }
                saved.Text = "Saved";
                discard.Visibility = Visibility.Collapsed;
            }
        }
        finally
        {
            saving = false;
            discard.IsEnabled = true;
            SyncControls();
        }
        if (closeRequested && lastSaveSucceeded && pending.Count == 0)
        {
            allowClose = true;
            Close();
        }
    }
    void Select(string name)
    {
        selectedPage = name;
        if (connectionHelp != null)
            connectionHelp.Visibility = name == "Help" ? Visibility.Collapsed : Visibility.Visible;
        foreach (var page in pages)
            page.Value.Visibility = page.Key == name ? Visibility.Visible : Visibility.Collapsed;
        foreach (var button in navigation)
            button.Value.Background = button.Key == name ? new SolidColorBrush(Color.FromRgb(40, 50, 66)) : Brushes.Transparent;
        foreach (var button in navigation)
            button.Value.Foreground = button.Key == name ? Ui.Ink : Ui.Muted;
        reset!.Visibility = name is "Tracking" or "Overlay" ? Visibility.Visible : Visibility.Collapsed;
    }
    public void ShowSection(string name)
    {
        name = name switch
        {
            "Tracker" => "Tracking",
            "Appearance" => "Overlay",
            "Accounts" or "General" => "Account",
            "Connection & Help" => "Help",
            _ => name
        };
        if (pages.ContainsKey(name))
            Select(name);
    }
    public void ShowConnection() => Select("Help");
    public void RefreshStatus()
    {
        RefreshUpdates();
        var view = host.View;
        state.Text = view?.Status ?? "Connecting to capture…";
        syncHelp.Text = view is { Devices: > 0, CollectionSynced: false } ? TrackingGuidance.Sync : "Collection syncs automatically at character login.";
        merchantStatus.Text = TrackingGuidance.Merchant(view, DateTimeOffset.UtcNow);
        merchantStatus.Visibility = view?.CollectionSynced == true && view.Ready ? Visibility.Collapsed : Visibility.Visible;
        connection.Text = ConnectionAssessment(view, host.ConnectionError);
        hotkeyStatus.Text = host.HotkeyError;
        hotkeyStatus.Visibility = host.HotkeyError.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        var target = view?.Profiles.FirstOrDefault(p => p.ProfileKey == view.Selected);
        bool needsLink = view?.Ready == false && view.DetectedCharacter is { } detected && target != null && !target.Matches(detected);
        linkCharacter.Visibility = needsLink ? Visibility.Visible : Visibility.Collapsed;
        linkCharacter.IsEnabled = needsLink;
        linkCharacter.Content = needsLink ? (view!.Profiles.Count == 1 ? "Use " + view.DetectedCharacter + " with " + target!.Label : "Choose account for " + view.DetectedCharacter) : "Link character";
        npcap!.Visibility = view?.Devices == 0 ? Visibility.Visible : Visibility.Collapsed;
        var h = view?.Health;
        diagnostics.Text = h == null ? "Waiting for connection diagnostics." : $"Decoded messages: {h.Frames}\nSoul rewards: {h.Rewards}\nDecoder faults: {h.DecoderFaults}\nActive stream interruptions: {h.ActiveInterruptions}\nLast collection: {view?.LastSync?.ToLocalTime().ToString("g") ?? "Not received"}";
        if (!saving && pending.Count == 0)
            SyncControls();
    }
}
