using System;

using System.IO;

using System.Linq;

using System.Collections.Generic;

using System.Windows;

using System.Windows.Controls;

using System.Windows.Media;

using System.Windows.Threading;

namespace PetTracker;

public static class UiChecks
{

    static int passed;

    static void Check(bool value, string message)
    {
        passed++;
        if (!value)
            throw new Exception(message);
    }

    static IEnumerable<DependencyObject> Children(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Children(child))
                yield return nested;
        }
    }

    static void ShowOffscreen(Window window)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.Topmost = false;
        window.Left = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth + 2048;
        window.Top = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight + 2048;
        window.Show();
    }

    static void Pump(int ms)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (s, e) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    [STAThread]
    public static void Main(string[] args)
    {
        if (QuitChecks.RunChild(args))
            return;
        string folder = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(folder);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(NativeTheme.Resources());
        using var host = new Host(Path.Combine(folder, "fixture"), false);
        var profile = new Profile { CharacterName = "Ranger", Pets = Catalog.Create() };
        profile.Pets[1050].Level = 2;
        profile.Pets[1050].Souls = 72;
        profile.Pets[1050].Goal = 75;
        var settings = new AppSettings { Visible = true, ExpireInactive = true, InactiveSeconds = 20 };
        var now = DateTimeOffset.UtcNow;
        var view = new WorkerView(1, profile, true, "Live", "Sample data", "Ranger", new(), "0123456789abcdef01234567", new() { new(1050, null, "", now.AddSeconds(-18.7)) }, null, now, 1, 0, 0, 0, settings);
        host.Preview(view);
        var overlay = host.Overlay!;
        Check(!overlay.IsVisible, "Render preview never opens a sample tracker window");
        ShowOffscreen(overlay);
        Pump(50);
        Check(overlay.Left > SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth && !overlay.Topmost, "Motion tests keep sample overlays outside the game desktop");
        // Start the short expiry window after WPF has loaded its theme and assets.
        // Cold CI startup can take longer than the fixture's remaining 1.3 seconds.
        now = DateTimeOffset.UtcNow;
        view = view with { Recent = new() { new(1050, null, "", now.AddSeconds(-18.7)) } };
        host.Preview(view);
        Pump(50);
        var row = Children(overlay).OfType<ContentControl>().Single(b => System.Windows.Automation.AutomationProperties.GetAutomationId(b) == "PetRow-1050");
        Check(row.HasAnimatedProperties, "Expiry schedules actual WPF animations");
        double first = row.Opacity;
        Program.Save(overlay, Path.Combine(folder, "fade-start.png"), 340, 140);
        Pump(350);
        Check(row.Opacity < first, "Actual opacity falls before expiry");
        Program.Save(overlay, Path.Combine(folder, "fade-middle.png"), 340, 140);
        Pump(1100);
        Check(overlay.DisplayedPetCount == 0, "Actual animation removes the expired row");
        Program.Save(overlay, Path.Combine(folder, "fade-finished.png"), 340, 140);

        view = view with
        {
            Recent = new() { new(1050, null, "", DateTimeOffset.UtcNow) }
        };
        host.Preview(view);
        Pump(30);
        row = Children(overlay).OfType<ContentControl>().Single(b => System.Windows.Automation.AutomationProperties.GetAutomationId(b) == "PetRow-1050");
        Check(row.Opacity > .98, "New activity restores full opacity");
        settings.ReducedMotion = true;
        host.Preview(view);
        Check(!row.HasAnimatedProperties, "Reduced motion cancels previous animation clocks");
        Program.Save(overlay, Path.Combine(folder, "renewed.png"), 340, 140);
        profile.Pets[1050].ProgressNeedsSync = true;
        host.Preview(view);
        Check(!Children(overlay).OfType<TextBlock>().Any(t => t.Text == "Sync needed"), "Recent rows omit repetitive sync labels");
        var staleTip = (ToolTip)row.ToolTip;
        Check(((TextBlock)staleTip.Content).Text.Contains("Last known soul total"), "Tooltip still distinguishes last-known soul totals");
        Program.Save(overlay, Path.Combine(folder, "tracker-clean.png"), 340, 140);

        using (var pendingHost = new Host(Path.Combine(folder, "pending-unlock-render"), false))
        {
            var pendingProfile = new Profile { CharacterName = "Fixture", Pets = Catalog.Create() };
            var pendingPet = pendingProfile.Pets[1007];
            pendingPet.Level = 0;
            pendingPet.LevelVerified = true;
            pendingPet.Souls = 4;
            pendingPet.EstimatedSouls = 5;
            pendingPet.Goal = 5;
            pendingPet.ProgressNeedsSync = true;
            var pendingView = view with
            {
                Profile = pendingProfile,
                Settings = new()
                {
                    Visible = true,
                    ReducedMotion = true,
                    ExpireInactive = false
                },
                Recent = new() { new(1007, null, "", DateTimeOffset.UtcNow) }
            };
            pendingHost.Preview(pendingView);
            var pendingOverlay = pendingHost.Overlay!;
            ShowOffscreen(pendingOverlay);
            Pump(30);
            Check(Children(pendingOverlay).OfType<TextBlock>().Any(t => t.Text == "Rank increase pending") && Children(pendingOverlay).OfType<TextBlock>().Any(t => t.Text == "5 / 5"), "An unconfirmed unlock renders the completed stage with its pending status");
            Program.Save(pendingOverlay, Path.Combine(folder, "unlock-pending.png"), 340, 160);
            pendingPet.Level = 1;
            pendingPet.Souls = 2;
            pendingPet.EstimatedSouls = 2;
            pendingPet.Goal = 25;
            pendingPet.ProgressNeedsSync = false;
            pendingHost.Preview(pendingView);
            Pump(30);
            Check(!Children(pendingOverlay).OfType<TextBlock>().Any(t => t.Text == "Rank increase pending") && Children(pendingOverlay).OfType<TextBlock>().Any(t => t.Text == "2 / 25"), "Confirmed rank replaces the pending status and displays the recovered balance");
            Program.Save(pendingOverlay, Path.Combine(folder, "unlock-confirmed.png"), 340, 160);
        }

        var remainingPet = new PetProgress { Level = 1, LevelVerified = true, Souls = 12, Goal = 25 };
        Check(TrackingGuidance.Remaining(remainingPet) == "13 souls remaining to reach Rank 2.", "Remaining tooltip uses current-stage balance");
        remainingPet.ProgressNeedsSync = true;
        Check(TrackingGuidance.Remaining(remainingPet) == "", "Stale progress does not claim remaining souls");
        remainingPet.ProgressNeedsSync = false;
        remainingPet.Souls = null;
        Check(TrackingGuidance.Remaining(remainingPet) == "", "Unknown progress does not claim remaining souls");
        remainingPet.Souls = 4;
        remainingPet.Goal = 5;
        remainingPet.Level = 0;
        Check(TrackingGuidance.Remaining(remainingPet) == "1 soul remaining to unlock Rank 1.", "Unlock tooltip uses the five-soul stage");
        remainingPet.Level = 3;
        Check(TrackingGuidance.Remaining(remainingPet) == "", "Maxed pet has no remaining-souls claim");
        Check(TrackingGuidance.Merchant(view with
        {
            Devices = 0
        }, now).Contains("capture connection"), "Merchant help prioritizes unavailable capture");
        Check(TrackingGuidance.Merchant(view with
        {
            CollectionSynced = false,
            Settings = new()
            {
                BlackMarketAlerts = true
            }
        }, now).Contains("Settings → Account"), "Merchant sync help points to the Account login instructions");
        Check(TrackingGuidance.Merchant(view with
        {
            CollectionSynced = true,
            Settings = new()
            {
                BlackMarketAlerts = true
            },
            Market = Array.Empty<MarketStock>()
        }, now).Contains("No fresh merchant stock"), "Missing stock is distinguished from no qualifying offers");
        Check(TrackingGuidance.Merchant(view with
        {
            CollectionSynced = true,
            Settings = new()
            {
                BlackMarketAlerts = true
            },
            Market = new[] { new MarketStock(104, now, now.AddHours(1), Array.Empty<MarketOffer>()) }
        }, now).Contains("Fresh merchant stock received, but no current recommendations"), "Observed stock without recommendations is explained without asserting no stock");
        var guidanceWindow = new SettingsWindow(host);
        ShowOffscreen(guidanceWindow);
        guidanceWindow.ShowSection("General");
        Pump(30);
        Check(Children(guidanceWindow).OfType<TextBlock>().Any(t => t.IsVisible && t.Text == TrackingGuidance.Sync), "Unsynced settings show login guidance");
        Program.Save(guidanceWindow, Path.Combine(folder, "merchant-sync-help.png"), 780, 700);
        foreach (var scroll in Children(guidanceWindow).OfType<ScrollViewer>().Where(s => s.IsVisible))
            scroll.ScrollToBottom();
        Pump(30);
        Program.Save(guidanceWindow, Path.Combine(folder, "merchant-help-detail.png"), 780, 700);
        guidanceWindow.Close();
        var window = new SettingsWindow(host);
        ShowOffscreen(window);
        Pump(30);
        var combo = Children(window).OfType<ComboBox>().First();
        Check(combo.Template.FindName("PART_Popup", combo) != null, "Dropdown provides the required WPF popup part");
        var checkbox = Children(window).OfType<CheckBox>().First();
        checkbox.IsChecked = true;
        Pump(10);
        Check(checkbox.IsChecked == true, "Custom checkbox remains interactive");
        window.ShowSection("Appearance");
        Pump(30);
        var sizeSlider = Children(window).OfType<Slider>().First();
        double beforeDrag = sizeSlider.Value;
        var sizeTrack = (System.Windows.Controls.Primitives.Track)sizeSlider.Template.FindName("PART_Track", sizeSlider);
        sizeTrack.Thumb.RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(60, 0) { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragDeltaEvent });
        Pump(350);
        Check(sizeSlider.Value > beforeDrag && Math.Abs(host.PetOverlayPreferences.Scale - sizeSlider.Value) < .001, "Actual slider drag previews and commits only its setting");
        Check(!Children(window).OfType<Button>().Any(b => Equals(b.Content, "Save settings") || Equals(b.Content, "Back up collections") || Equals(b.Content, "Export settings")), "Settings contain no manual save or data-management tasks");
        window.ShowConnection();
        Pump(30);
        foreach (var section in Children(window).OfType<Expander>().ToArray())
            section.IsExpanded = true;
        Pump(30);
        Check(Children(window).OfType<Button>().Any(b => Equals(b.Content, "Record activity check (90 seconds)")), "Connection help exposes an activity check without restarting capture");
        Check(Children(window).OfType<Button>().Any(b => Equals(b.Content, "Save support report…")) && Children(window).OfType<Button>().Any(b => Equals(b.Content, "Copy support report")), "Connection help offers a privacy-filtered shareable report: " + string.Join(",", Children(window).OfType<Button>().Select(b => b.Content)));
        Program.Save(window, Path.Combine(folder, "connection-check.png"), 780, 700);
        window.Close();
        view = view with
        {
            Ready = false,
            Status = "Waiting for your character…"
        };
        host.Preview(view);
        Pump(30);
        Program.Save(overlay, Path.Combine(folder, "waiting-character.png"), 340, 110);
        view = view with
        {
            Ready = false,
            Status = "Encore detected — choose profile",
            Detail = "Selected profile: Zero · Siel. Open Settings → Accounts to link Encore."
        };
        host.Preview(view);
        Pump(30);
        Check(Children(overlay).OfType<TextBlock>().Any(t => t.Text == "Encore detected — choose profile"), "Empty tracker displays the actual profile mismatch");
        Program.Save(overlay, Path.Combine(folder, "character-mismatch.png"), 340, 140);
        var accountBinding = new LocalProfileBinding { AccountName = "My account", CharacterName = "Zero", RealmLabel = "Siel", ProfileKey = "0123456789abcdef01234567" };
        view = view with
        {
            Profiles = new() { accountBinding },
            DetectedCharacter = "Encore",
            Status = "Encore — link to account"
        };
        host.Preview(view);
        var altSettings = new SettingsWindow(host);
        ShowOffscreen(altSettings);
        Pump(30);
        Check(Children(altSettings).OfType<Button>().Any(b => Equals(b.Content, "Use Encore with My account") && b.IsVisible), "Detected alt has a direct linking action in the settings header");
        Program.Save(altSettings, Path.Combine(folder, "alt-link-settings.png"), 780, 700);
        view = view with
        {
            Ready = true
        };
        host.Preview(view);
        altSettings.RefreshStatus();
        Check(!Children(altSettings).OfType<Button>().Any(b => Equals(b.Content, "Use Encore with My account") && b.IsVisible), "Account action disappears once the character connects");
        altSettings.Close();
        view = view with
        {
            Ready = false
        };
        host.Preview(view);
        var profileDialog = new AccountsWindow(host);
        ShowOffscreen(profileDialog);
        Pump(30);
        Check(Children(profileDialog).OfType<Button>().Any(b => Equals(b.Content, "Use Encore with My account")), "Standalone accounts dialog offers linking the detected alt");
        Check(Children(profileDialog).OfType<TextBlock>().Any(t => t.Text == "My account"), "Selected account renders its player label");
        Program.Save(profileDialog, Path.Combine(folder, "account-profiles.png"), 500, 540);
        profileDialog.Close();
        Check(!Children(overlay).OfType<Button>().Any(b => Equals(b.Content, "↗")), "Tracker header contains no collection launcher");
        Check(typeof(Host).Assembly.GetType("PetTracker.CollectionWindow") == null, "App contains no duplicate collection window");
        using (var visibility = new Host(Path.Combine(folder, "visibility-case"), false))
        {
            visibility.IsGameForeground = _ => false;
            var restricted = view with
            {
                Ready = true,
                Status = "Live",
                Settings = new()
                {
                    Visible = true,
                    GameOnly = true
                }
            };
            visibility.Preview(restricted);
            Check(visibility.Overlay == null, "Game-only mode creates no hidden preview window outside the game");
            visibility.IsGameForeground = _ => true;
            visibility.Preview(restricted);
            var same = visibility.Overlay!;
            ShowOffscreen(same);
            Pump(20);
            int shown = 0;
            same.IsVisibleChanged += (s, e) =>
            {
                if ((bool)e.NewValue)
                    shown++;
            };
            visibility.IsGameForeground = _ => false;
            visibility.Preview(restricted);
            visibility.Preview(restricted);
            Check(!same.IsVisible && shown == 0, "Status polling never flashes a hidden game-only overlay");
            visibility.IsGameForeground = _ => true;
            visibility.Preview(restricted);
            Check(ReferenceEquals(same, visibility.Overlay), "Focus changes reuse one tracker instance");
        }
        using (var updateHost = new Host(Path.Combine(folder, "updates-case"), false, new PetTracker.Updates.UpdateSession(_ => new UpdatePreviewClient())))
        {

            updateHost.Preview(view with
            {
                Settings = new()
                {
                    UpdateChannel = "win-stable"
                }
            });

            var updateWindow = new SettingsWindow(updateHost);
            ShowOffscreen(updateWindow);
            updateWindow.ShowSection("Updates");
            Pump(30);

            var checkButton = Children(updateWindow).OfType<Button>().Single(b => Equals(b.Content, "Check for updates"));
            checkButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump(60);

            Check(Children(updateWindow).OfType<Button>().Any(b => Equals(b.Content, "Download update") && b.IsVisible), "Updates page exposes a download after a verified check");

            Children(updateWindow).OfType<Button>().Single(b => Equals(b.Content, "Download update")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump(60);

            Check(Children(updateWindow).OfType<Button>().Any(b => Equals(b.Content, "Restart to update") && b.IsVisible), "Downloaded update offers a player-controlled restart");

            Program.Save(updateWindow, Path.Combine(folder, "updates-ready.png"), 780, 700);

            Children(updateWindow).OfType<Expander>().Single(e => Equals(e.Header, "Advanced update options")).IsExpanded = true;
            Pump(30);
            var channel = Children(updateWindow).OfType<ComboBox>().Single(b => b.Items.Contains("Stable"));
            channel.SelectedItem = "Beta";
            Pump(400);
            updateWindow.RefreshStatus();

            Check(!Children(updateWindow).OfType<Button>().Single(b => Equals(b.Content, "Restart to update")).IsVisible, "Changing channel hides the old channel's restart action");
            updateWindow.Close();

        }

        using (var firstRunPreview = new Host(Path.Combine(folder, "first-run-preview"), false))
        {
            firstRunPreview.Preview(view with
            {
                Ready = false,
                Status = "Waiting for character login",
                Detail = "Log into your character to load pet ranks and soul totals automatically. If you are already playing, return to character selection and log in again. No account setup is needed.",
                Profiles = new(),
                Selected = null,
                Recent = new(),
                Settings = new()
                {
                    Visible = true
                }
            });
            var firstRunOverlay = firstRunPreview.Overlay!;
            ShowOffscreen(firstRunOverlay);
            Pump(20);
            var loginPrompt = Children(firstRunOverlay).OfType<TextBlock>().Single(t => t.Text == "Waiting for character login");
            Check(loginPrompt.ToolTip is ToolTip tip && tip.Content is TextBlock help && help.Text.Contains("No account setup is needed"), "First-run tracker shows login guidance and explains automatic account setup");
            Program.Save(firstRunOverlay, Path.Combine(folder, "first-run-login.png"), 340, 140);
            var firstRunSettings = new SettingsWindow(firstRunPreview);
            ShowOffscreen(firstRunSettings);
            firstRunSettings.ShowConnection();
            Pump(20);
            Check(!Children(firstRunSettings).OfType<Button>().Any(b => b.IsVisible && (Equals(b.Content, "Link character") || Equals(b.Content, "Use selected account"))), "Fresh installation does not expose a required account-selection action");
            Program.Save(firstRunSettings, Path.Combine(folder, "first-run-help.png"), 780, 700);
            firstRunSettings.Close();
        }
        view = view with
        {
            Ready = true,
            Status = "Live",
            Settings = new()
            {
                PinnedPetId = 1050,
                ExpireInactive = true,
                InactiveSeconds = 20
            },
            Recent = new()
        };
        host.Preview(view);
        Pump(30);

        Check(overlay.DisplayedPetCount == 1, "Pin appears without recent activity");

        var pinnedRow = Children(overlay).OfType<ContentControl>().Single(b => System.Windows.Automation.AutomationProperties.GetAutomationId(b) == "PetRow-1050");

        Check(!pinnedRow.HasAnimatedProperties && pinnedRow.Opacity == 1, "Pinned target never fades or collapses");

        Program.Save(overlay, Path.Combine(folder, "pinned-tracker.png"), 340, 145);

        var finder = new PetFinderWindow(host);
        ShowOffscreen(finder);
        Pump(30);

        var finderSearch = Children(finder).OfType<TextBox>().Single();
        finderSearch.Text = "Foul Skama";
        Pump(30);

        Check(Children(finder).OfType<ListBox>().Single().Items.Count == 1, "Finder searches mobs outside recent activity");

        Check(Children(finder).OfType<Button>().Any(b => Equals(b.Content, "Verteron ↗")), "Finder offers the verified region map");

        Program.Save(finder, Path.Combine(folder, "pet-finder.png"), 860, 640);
        finderSearch.Text = "Young Kailin";
        Pump(30);
        Check(!Children(finder).OfType<Button>().Any(b => (b.Content?.ToString() ?? "").Contains("Eltnen") || (b.Content?.ToString() ?? "").Contains("Morheim")), "Finder hides unavailable region buttons");
        Check(!Children(finder).OfType<TextBlock>().Any(t => t.Text.Contains("Global launch data") || t.Text.Contains("Asian-release")), "Finder omits edition metadata from the main view");
        Program.Save(finder, Path.Combine(folder, "finder-modern.png"), 860, 640);
        finder.Width = 720;
        finder.Height = 500;
        Pump(40);
        Program.Save(finder, Path.Combine(folder, "finder-compact.png"), 720, 500);
        finder.Width = 860;
        finder.Height = 640;
        Pump(40);
        finderSearch.Text = "Foul Skama";
        Pump(30);
        var stableMapButton = Children(finder).OfType<Button>().Single(b => Equals(b.Content, "Verteron ↗"));
        host.Preview(view);
        Pump(30);
        Check(Children(finder).Contains(stableMapButton), "Background status refresh preserves map controls and reading position");
        var finderList = Children(finder).OfType<ListBox>().Single();
        int listedId = PetFinder.Search("Foul Skama").Single().Pet.Id;
        var savedListedPet = profile.Pets[listedId];
        profile.Pets[listedId] = new PetProgress { Id = listedId, Level = 2, Souls = 12, Goal = 75 };
        host.Preview(view);
        Pump(30);
        var listedContainer = (ListBoxItem)finderList.ItemContainerGenerator.ContainerFromIndex(0);
        Check(Children(listedContainer).OfType<TextBlock>().Any(t => t.Text == "Rank 2") && Children(listedContainer).OfType<TextBlock>().Any(t => t.Text == "12 / 75"), "Finder row binds the selected account rank and soul balance");
        var stableSelection = finderList.SelectedItem;
        profile.Pets[listedId].Souls = 13;
        host.Preview(view);
        Pump(30);
        Check(ReferenceEquals(finderList.SelectedItem, stableSelection) && ReferenceEquals(finderList.ItemContainerGenerator.ContainerFromIndex(0), listedContainer) && Children(listedContainer).OfType<TextBlock>().Any(t => t.Text == "13 / 75"), "Reward refresh updates finder progress without replacing the selection or row");
        Check(Children(finder).OfType<TextBlock>().Count(t => t.Text == "13 / 75") >= 2, "Selected pet detail and list both update after a reward");
        var finderProgress = Children(finder).OfType<ProgressBar>().Single(p => System.Windows.Automation.AutomationProperties.GetAutomationId(p) == "FinderSoulProgress");
        Check(finderProgress.Visibility == Visibility.Visible && Math.Abs(finderProgress.Value - 100.0 * 13 / 75) < .001, "Finder progress bar follows live soul updates without rebuilding the detail");
        profile.Pets[listedId].Level = 3;
        host.Preview(view);
        Pump(30);
        Check(Children(listedContainer).OfType<TextBlock>().Any(t => t.Text == "Rank 3") && Children(listedContainer).OfType<TextBlock>().Any(t => t.Text == "\u2713 MAX"), "Finder marks completed pets as MAX");
        Check(finderProgress.Value == 100, "Finder progress bar shows a completed rank as full");
        profile.Pets[listedId] = new PetProgress { Id = listedId, Level = 2, Goal = 75 };
        host.Preview(view);
        Pump(30);
        Check(Children(listedContainer).OfType<TextBlock>().Any(t => t.Text == "? / 75"), "Finder preserves an unknown balance instead of inventing zero souls");
        Check(finderProgress.Visibility == Visibility.Collapsed, "Finder hides the progress bar when the soul balance is unknown");
        profile.Pets[listedId] = savedListedPet;
        host.Preview(view);
        Pump(30);


        Children(finder).OfType<Button>().Single(b => Equals(b.Content, "Pin this pet")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump(40);

        Check(host.PetOverlayPreferences.IsPinned(PetFinder.Search("Foul Skama").Single().Pet.Id), "Pin this pet saves the selected target through a pin command");

        Check(Children(finder).OfType<Button>().Any(b => Equals(b.Content, "Unpin pet")), "Finder updates its pin action after save");

        Children(finder).OfType<Button>().Single(b => Equals(b.Content, "Unpin pet")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump(40);

        Check(host.PetOverlayPreferences.Pins().SequenceEqual(new[] { 1050 }), "Unpin removes the selected pet while preserving the older pin");

        // Multi-pin controls share capacity and never discard a different pinned goal.
        finderSearch.Text = "";
        Pump(30);
        var extraPins = PetFinder.Search("", profile).Select(p => p.Pet.Id).Where(id => id != 1050).Take(3).ToArray();
        foreach (int id in extraPins.Take(2))
        {
            finder.SelectPet(id);
            Pump(20);
            Children(finder).OfType<Button>().Single(b => Equals(b.Content, "Pin this pet")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump(30);
        }
        Check(host.PetOverlayPreferences.Pins().SequenceEqual(new[] { 1050 }.Concat(extraPins.Take(2))), "Finder adds several pets without replacing an earlier pin");
        Check(overlay.DisplayedPetCount == 3 && Children(overlay).OfType<TextBlock>().Count(t => t.Text == "Pinned") == 3, "Overlay shows three individually marked pins without recent activity");
        Program.Save(overlay, Path.Combine(folder, "multi-pinned-tracker.png"), 340, 335);
        finder.SelectPet(extraPins[2]);
        Pump(20);
        var fullPin = Children(finder).OfType<Button>().Single(b => Equals(b.Content, "Pin limit reached"));
        Check(!fullPin.IsEnabled && ToolTipService.GetShowOnDisabled(fullPin), "Finder explains full capacity instead of replacing a pinned pet");
        Program.Save(finder, Path.Combine(folder, "finder-pin-limit.png"), 860, 640);
        var pinSettings = new SettingsWindow(host);
        ShowOffscreen(pinSettings);
        Pump(20);
        var pinLimit = Children(pinSettings).OfType<ComboBox>().Single(c => System.Windows.Automation.AutomationProperties.GetAutomationId(c) == "Maximum pinned pets");
        Check(Equals(pinLimit.SelectedItem, 3), "Pin limit defaults to three in Tracking settings");
        pinLimit.SelectedItem = 5;
        Pump(350);
        Check(host.PetOverlayPreferences.PinLimit == 5 && Children(finder).OfType<Button>().Any(b => Equals(b.Content, "Pin this pet") && b.IsEnabled), "Raising the limit enables the selected pet without reopening Finder");
        Program.Save(pinSettings, Path.Combine(folder, "pin-settings.png"), 780, 700);
        Children(finder).OfType<Button>().Single(b => Equals(b.Content, "Pin this pet")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump(30);
        pinLimit.SelectedItem = 1;
        Pump(350);
        Check(host.PetOverlayPreferences.Pins().Count == 4 && Children(finder).OfType<Button>().Any(b => Equals(b.Content, "Unpin pet") && b.IsEnabled), "Lowering pin limit keeps existing pins removable");
        var multiRow = Children(overlay).OfType<ContentControl>().Single(b => System.Windows.Automation.AutomationProperties.GetAutomationId(b) == "PetRow-" + extraPins[0]);
        var pinMenu = multiRow.ContextMenu!;
        pinMenu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
        var unpinMenu = pinMenu.Items.OfType<MenuItem>().Single(m => Equals(m.Header, "Unpin pet"));
        unpinMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Pump(30);
        Check(!host.PetOverlayPreferences.IsPinned(extraPins[0]) && host.PetOverlayPreferences.IsPinned(extraPins[1]), "Overlay context menu unpins only the clicked row");
        pinSettings.Close();
        host.Preview(view);
        Pump(30);

        finderSearch.Text = "rabbit";
        Pump(30);
        Check(Children(finder).OfType<Button>().Any(b => Equals(b.Content, "Official source")), "Shop pet has an official acquisition source");
        Check(!Children(finder).OfType<TextBlock>().Any(t => t.Text == "Farming maps" || t.Text == "Linked mobs"), "Shop pet omits empty farming and mob panels");
        Program.Save(finder, Path.Combine(folder, "finder-shop.png"), 860, 640);
        finderSearch.Text = "twitch";
        Pump(30);
        Check(Children(finder).OfType<TextBlock>().Any(t => t.Text.Contains("four-hour")), "Twitch reward explains how it was earned");
        Program.Save(finder, Path.Combine(folder, "finder-twitch.png"), 860, 640);
        finderSearch.Text = "Aberrant Odyle Spider";
        finder.SelectPet(1021);
        Pump(30);
        Check(Children(finder).OfType<TextBlock>().Any(t => t.Text.Contains("Pet Soul Chest: Fera")), "Chest-only spider explains its soul acquisition route");
        Check(!Children(finder).OfType<TextBlock>().Any(t => t.Text.Contains("No confirmed farming location") || t.Text == "Locations") && !Children(finder).OfType<Button>().Any(b => Equals(b.Content, "Official source")), "Chest entry avoids an empty map section and a false official-source label");
        Program.Save(finder, Path.Combine(folder, "finder-spider.png"), 860, 640);
        finder.Width = 720;
        finder.Height = 500;
        Pump(30);
        Program.Save(finder, Path.Combine(folder, "finder-spider-compact.png"), 720, 500);
        finder.Width = 860;
        finder.Height = 640;
        finder.SelectPet(1077);
        Pump(30);
        Check(Children(finder).OfType<TextBlock>().Any(t => t.Text == "Krao Cave") && Children(finder).OfType<TextBlock>().Any(t => t.Text == "Enhanced Harcon"), "Dungeon detail renders the real mob and its dungeon");
        Program.Save(finder, Path.Combine(folder, "finder-dungeon.png"), 860, 640);
        finder.SelectPet(1363);
        Pump(30);
        Check(Children(finder).OfType<Image>().Any(i => i.Source != null) && Children(finder).OfType<Button>().Any(b => Equals(b.Content, "Official source")), "New shop pet has artwork and its official notice");
        Program.Save(finder, Path.Combine(folder, "finder-halloween.png"), 860, 640);
        finderSearch.Text = "Pet #";
        Pump(30);
        Check(Children(finder).OfType<ListBox>().Single().Items.Count == 0, "Unknown catalog placeholders never appear in finder results");
        finderSearch.Text = "Aberrant";
        Pump(30);
        var samples = PetFinder.Search("Aberrant").Take(3).Select(p => p.Pet.Id).ToArray();
        var sampleOriginals = samples.Select(id => profile.Pets[id]).ToArray();
        profile.Pets[samples[0]] = new PetProgress { Id = samples[0], Level = 2, Souls = 18, Goal = 75 };
        profile.Pets[samples[1]] = new PetProgress { Id = samples[1], Level = 1, Souls = 4, Goal = 25 };
        profile.Pets[samples[2]] = new PetProgress { Id = samples[2], Level = 3 };
        host.Preview(view);
        Pump(30);
        Program.Save(finder, Path.Combine(folder, "finder-progress.png"), 860, 640);
        finderSearch.Text = "";
        finder.SelectPet(samples[0]);
        Pump(30);
        Program.Save(finder, Path.Combine(folder, "finder-collection.png"), 860, 640);
        for (int i = 0; i < samples.Length; i++)
            profile.Pets[samples[i]] = sampleOriginals[i];
        host.Preview(view);
        finderSearch.Text = "does not exist 9988";
        Pump(20);
        Check(Children(finder).OfType<TextBlock>().Any(t => t.Text.StartsWith("No matching pets")), "Finder explains an empty search");
        var marketFilter = Children(finder).OfType<ComboBox>().Single(c => System.Windows.Automation.AutomationProperties.GetAutomationId(c) == "PetFinderFilter");
        finderSearch.Text = "";
        marketFilter.SelectedIndex = 1;
        Pump(30);
        Check(finderList.Items.Count == 42 && Children(finder).OfType<TextBlock>().Any(t => t.Text.StartsWith("Shopping list") && t.Text.Contains("dungeon pets")), "Market watchlist includes dungeon pets without claiming live availability");
        finderSearch.Text = "Phantasmal Lakshmi";
        Pump(30);
        Check(finderList.Items.Count == 1, "Merchant search includes Phantasmal Lakshmi");
        Program.Save(finder, Path.Combine(folder, "merchant-dungeon-search.png"), 860, 640);
        finderSearch.Text = "Elite Dratona";
        Pump(30);
        Check(finderList.Items.Count == 1, "Market watchlist search finds a non-farmable pet");
        var originalMarketPet = profile.Pets[1196];
        profile.Pets[1196] = new PetProgress { Id = 1196, Level = 2, LevelVerified = true, Souls = 5, Goal = 75 };
        var marketNow = DateTimeOffset.UtcNow;
        var marketStock = new MarketStock(101, marketNow, marketNow.AddMinutes(30), new[] { new MarketOffer(111097, 534760190, 1196, 80000) });
        var marketView = view with
        {
            Market = new[] { marketStock },
            CollectionSynced = true
        };
        host.Preview(marketView);
        marketFilter.SelectedIndex = 2;
        Pump(30);
        Check(finderList.Items.Count == 1 && Children(finder).OfType<TextBlock>().Any(t => t.Text.Contains("80,000 Kina")), "Recent market view renders the captured price and matching collection rank");
        Check(overlay.DisplayedMerchantCount == 1 && Children(overlay).OfType<TextBlock>().Any(t => t.Text.Contains("80,000 Kina")), "Overlay shows the needed merchant pet and observed price");
        Program.Save(overlay, Path.Combine(folder, "merchant-overlay.png"), 340, 360);
        var merchantGrid = Children(overlay).OfType<Grid>().Single(g => System.Windows.Automation.AutomationProperties.GetAutomationId(g) == "MerchantPet-1196");
        host.Preview(marketView);
        host.Preview(marketView);
        Check(overlay.DisplayedMerchantCount == 1, "Repeated polling keeps the shopping list visible");
        Check(ReferenceEquals(merchantGrid, Children(overlay).OfType<Grid>().Single(g => System.Windows.Automation.AutomationProperties.GetAutomationId(g) == "MerchantPet-1196")), "Unchanged merchant rows stay attached between polls");
        Check(Children(merchantGrid).OfType<TextBlock>().Any(t => t.Text == "Rank 2 · 5/75 souls"), "Merchant row shows current rank and stage balance beside its offer");
        profile.Pets[1196].Souls = 10;
        host.Preview(marketView);
        Check(Children(overlay).OfType<TextBlock>().Any(t => t.Text == "Rank 2 · 10/75 souls") && !Children(overlay).OfType<TextBlock>().Any(t => t.Text == "Rank 2 · 5/75 souls"), "Soul balance changes refresh merchant rows even when price and rank are unchanged");
        profile.Pets[1196].Souls = null;
        host.Preview(marketView);
        Check(Children(overlay).OfType<TextBlock>().Any(t => t.Text == "Rank 2 · ?/75 souls"), "Missing merchant counter is never displayed as zero");
        profile.Pets[1196].EstimatedSouls = 12;
        host.Preview(marketView);
        Check(Children(overlay).OfType<TextBlock>().Any(t => t.Text == "Rank 2 · ≈12/75 souls"), "Merchant progress preserves the estimated-balance marker");
        profile.Pets[1196].EstimatedSouls = null;
        profile.Pets[1196].Souls = 5;
        host.Preview(marketView);
        host.Preview(marketView with
        {
            CollectionSynced = false
        });
        Check(overlay.DisplayedMerchantCount == 0, "Unsynchronized collection clears shopping recommendations");
        host.Preview(marketView);
        var shoppingProfile = new Profile { Pets = Catalog.Create() };
        int[] shoppingIds = { 1107, 1145, 1177 };
        foreach (int id in shoppingIds)
        {
            shoppingProfile.Pets[id].Level = 1;
            shoppingProfile.Pets[id].LevelVerified = true;
            shoppingProfile.Pets[id].ProgressNeedsSync = false;
            shoppingProfile.Pets[id].Souls = id == 1145 ? 10 : 5;
            shoppingProfile.Pets[id].Goal = 25;
        }
        var shoppingOffers = shoppingIds.Select((id, i) => new MarketOffer(i + 1, BlackMarket.Items.First(m => m.PetId == id).ItemId, id, new long[] { 70000, 50000, 80000 }[i])).ToArray();
        host.Preview(marketView with
        {
            Profile = shoppingProfile,
            Recent = new(),
            Nearby = Array.Empty<NearbyPet>(),
            Settings = new()
            {
                HideWhenEmpty = true,
                Compact = true
            },
            Market = new[] { new MarketStock(104, marketNow, marketNow.AddMinutes(30), shoppingOffers) }
        });
        Pump(30);
        Check(overlay.DisplayedMerchantCount == 3 && overlay.DisplayedPetCount == 0, "Shopping offers remain available with empty farming activity and Hide when empty enabled");
        Program.Save(overlay, Path.Combine(folder, "merchant-overlay-three.png"), 300, 380);
        host.Preview(marketView);
        using (var completionHost = new Host(Path.Combine(folder, "merchant-completion"), false))
        {
            var clock = DateTimeOffset.UtcNow;
            var completionView = marketView with
            {
                Profile = shoppingProfile,
                Recent = new(),
                Nearby = Array.Empty<NearbyPet>(),
                Settings = new()
                {
                    TrackerMode = "Nearby",
                    HideWhenEmpty = true,
                    Compact = true
                },
                Market = new[] { new MarketStock(104, clock.AddSeconds(-1), clock.AddMinutes(30), shoppingOffers) }
            };
            completionHost.Preview(completionView);
            var completedOverlay = new PetTrackerOverlay(completionHost, () => clock, false) { Width = 300 };
            ShowOffscreen(completedOverlay);
            Pump(20);
            Check(completedOverlay.DisplayedMerchantCount == 3, "Nearby shows the three offers before purchase");
            shoppingProfile.Pets[1145].Souls = 15;
            completionView = completionView with
            {
                Market = Array.Empty<MarketStock>(),
                Recent = new() { new(1145, null, "", clock, SoulRewardUtc: clock) }
            };
            completionHost.Preview(completionView);
            completedOverlay.Refresh();
            Check(completedOverlay.DisplayedMerchantCount == 1 && completedOverlay.DisplayedPetCount == 0, "Nearby retains only the rewarded offer when shop stock clears");
            Check(Children(completedOverlay).OfType<TextBlock>().Any(t => t.Text == "Rank 1 · 15/25 souls") && !Children(completedOverlay).OfType<TextBlock>().Any(t => t.Text.Contains("clears after") || t.Text.EndsWith(" Kina")), "Completed merchant row shows updated souls without countdown chatter or another purchase price");
            Check(completionHost.PetOverlayPreferences.TrackerMode == "Nearby", "Merchant feedback never switches the selected tracker mode");
            var retained = Children(completedOverlay).OfType<Grid>().Single(g => System.Windows.Automation.AutomationProperties.GetAutomationId(g) == "MerchantPet-1145");
            completedOverlay.Refresh();
            Check(ReferenceEquals(retained, Children(completedOverlay).OfType<Grid>().Single(g => System.Windows.Automation.AutomationProperties.GetAutomationId(g) == "MerchantPet-1145")), "Repeated completion polling does not recreate the merchant row");
            Program.Save(completedOverlay, Path.Combine(folder, "merchant-updated-nearby.png"), 300, 230);
            clock = clock.AddSeconds(19);
            completedOverlay.UpdateStatus();
            Check(completedOverlay.DisplayedMerchantCount == 1, "Merchant feedback remains visible before its twenty-second deadline");
            completionHost.Preview(completionView with
            {
                Settings = new()
                {
                    TrackerMode = "Recent",
                    HideWhenEmpty = true,
                    Compact = true
                }
            });
            completedOverlay.Refresh();
            Check(completedOverlay.DisplayedMerchantCount == 1, "Switching to Recent preserves the same completion feedback");
            clock = clock.AddSeconds(1);
            completedOverlay.UpdateStatus();
            Check(completedOverlay.DisplayedMerchantCount == 0, "Completion expires in Recent mode without another reward");
            completedOverlay.DisposeOverlay();
        }
        Program.Save(finder, Path.Combine(folder, "finder-market-offer.png"), 860, 640);
        finder.Width = 720;
        finder.Height = 500;
        Pump(30);
        Program.Save(finder, Path.Combine(folder, "finder-market-compact.png"), 720, 500);
        finder.Width = 860;
        finder.Height = 640;
        host.Preview(marketView with
        {
            Market = new[] { marketStock with { SeenUtc = marketNow.AddMinutes(-3) } }
        });
        Pump(30);
        Check(finderList.Items.Count == 0 && Children(finder).OfType<TextBlock>().Any(t => t.Text.StartsWith("No recent matching shop offers")), "Expired observed stock disappears with actionable empty-state guidance");
        Check(overlay.DisplayedMerchantCount == 0, "Expired offers also leave the merchant overlay");
        marketFilter.SelectedIndex = 1;
        profile.Pets[1196].Level = 3;
        host.Preview(marketView);
        Pump(30);
        Check(finderList.Items.Count == 0 && overlay.DisplayedMerchantCount == 0, "Maxed pets leave the watchlist and cannot trigger a second alert");
        profile.Pets[1196] = originalMarketPet;
        host.Preview(view);
        finder.Close();
        host.Preview(view with
        {
            Devices = 0,
            Ready = false,
            Status = "Capture unavailable"
        });
        var setupWindow = new SettingsWindow(host);
        ShowOffscreen(setupWindow);
        setupWindow.ShowConnection();
        Pump(30);
        Check(Children(setupWindow).OfType<Button>().Any(b => Equals(b.Content, "Download Npcap") && b.IsVisible), "Missing capture exposes official Npcap download without expanding advanced help");
        Children(setupWindow).OfType<Button>().Single(b => Equals(b.Content, "Refresh connection status")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump(30);
        Check(Children(setupWindow).OfType<TextBlock>().Any(t => t.Text == "Connection status refreshed"), "Refresh action gives visible completion feedback");
        Program.Save(setupWindow, Path.Combine(folder, "capture-setup.png"), 780, 700);
        setupWindow.Close();
        host.Preview(view);
        var marketSettings = new SettingsWindow(host);
        ShowOffscreen(marketSettings);
        marketSettings.ShowSection("Tracking");
        Pump(30);
        var marketToggle = Children(marketSettings).OfType<CheckBox>().Single(c => Equals(c.Content, "Show merchant shopping list in overlay"));
        marketToggle.IsChecked = false;
        Pump(350);
        Check(!host.PetOverlayPreferences.BlackMarketAlerts, "Black Market notification control saves independently of rank notifications");
        marketToggle.BringIntoView();
        Pump(30);
        Program.Save(marketSettings, Path.Combine(folder, "settings-market.png"), 780, 700);
        marketSettings.Close();
        host.Preview(view);

        view = view with
        {
            Ready = true,
            Status = "Live",
            Settings = new()
            {
                TrackerMode = "Nearby",
                HideMaxed = true,
                NearbyStaleSeconds = 120,
                ExpireInactive = true,
                InactiveSeconds = 20
            },
            Recent = new() { new(1050, null, "Recent creature", DateTimeOffset.UtcNow) },
            Nearby = new[] { new NearbyPet(1060, "Coastal Starturtle", DateTimeOffset.UtcNow.AddSeconds(-35)), new NearbyPet(1050, "Gravi", DateTimeOffset.UtcNow.AddSeconds(-30)) }
        };
        profile.Pets[1060].Level = 1;
        profile.Pets[1060].Souls = 18;
        profile.Pets[1060].Goal = 25;
        profile.Pets[1060].ProgressNeedsSync = false;
        host.Preview(view);
        Pump(30);
        Check(overlay.DisplayedPetCount == 2, "Nearby UI shows untouched mobs beyond the Recent expiry timer");
        var nearbyRows = Children(overlay).OfType<ContentControl>().Where(b => System.Windows.Automation.AutomationProperties.GetAutomationId(b).StartsWith("PetRow-")).ToArray();
        Check(nearbyRows.All(b => !b.HasAnimatedProperties && b.Opacity == 1), "Nearby rows never run Recent fade animations");
        Check(Children(overlay).OfType<TextBlock>().Count(t => t.Text.StartsWith("Last seen ")) == 2, "Aging nearby rows visibly disclose their observation age");
        Program.Save(overlay, Path.Combine(folder, "nearby-tracker.png"), 340, 275);
        using (var stableHost = new Host(Path.Combine(folder, "stable-list"), false))
        {
            var stableProfile = new Profile { Pets = Catalog.Create() };
            int[] ids = stableProfile.Pets.Keys.Take(4).ToArray();
            foreach (int id in ids)
            {
                stableProfile.Pets[id].Level = 3;
                stableProfile.Pets[id].Goal = 75;
            }
            var stamp = DateTimeOffset.UtcNow;
            var stableView = view with
            {
                Profile = stableProfile,
                Settings = new()
                {
                    TrackerMode = "Nearby",
                    HideMaxed = false,
                    RowLimit = 3
                },
                Nearby = ids.Select((id, i) => new NearbyPet(id, "Mob", stamp.AddSeconds(-i))).ToArray()
            };
            stableHost.Preview(stableView);
            var stableOverlay = stableHost.Overlay!;
            ShowOffscreen(stableOverlay);
            Pump(30);
            ContentControl[] StableRows() => Children(stableOverlay).OfType<ContentControl>().Where(c => System.Windows.Automation.AutomationProperties.GetAutomationId(c).StartsWith("PetRow-")).ToArray();
            var initialRows = StableRows();
            int detached = 0;
            foreach (var stableRow in initialRows)
                stableRow.Unloaded += (s, e) => detached++;
            Program.Save(stableOverlay, Path.Combine(folder, "stable-list-before.png"), 340, 315);
            for (int step = 0; step < 6; step++)
            {
                stableHost.Preview(stableView with
                {
                    Nearby = ids.Reverse().Select((id, i) => new NearbyPet(id, "Mob", stamp.AddSeconds(-((i + step) % 4)))).ToArray()
                });
                Pump(35);
                Check(StableRows().SequenceEqual(initialRows), "Repeated alternating mob refreshes preserve rendered row order and membership");
            }
            Check(detached == 0, "Unchanged rows remain attached during status refreshes");
            Program.Save(stableOverlay, Path.Combine(folder, "stable-list-after.png"), 340, 315);
            stableHost.Preview(stableView with
            {
                Nearby = Array.Empty<NearbyPet>()
            });
            Pump(30);
            Check(stableOverlay.DisplayedPetCount == 0 && StableRows().Length == 0, "Stable row reconciliation still removes departed mobs");
        }
        using (var ageHost = new Host(Path.Combine(folder, "nearby-age-case"), false))
        {
            var clock = DateTimeOffset.UtcNow;
            var ageView = view with
            {
                Settings = new()
                {
                    TrackerMode = "Nearby",
                    NearbyStaleSeconds = 30,
                    PinnedPetId = 1050
                },
                Nearby = new[] { new NearbyPet(1050, "Gravi", clock) }
            };
            ageHost.Preview(ageView);
            var ageOverlay = new PetTrackerOverlay(ageHost, () => clock);
            ShowOffscreen(ageOverlay);
            Pump(20);
            clock = clock.AddSeconds(16);
            ageOverlay.UpdateStatus();
            Check(Children(ageOverlay).OfType<TextBlock>().Any(t => t.Text == "Pinned · Last seen 16s ago"), "Observation age updates without new worker data");
            clock = clock.AddSeconds(14);
            ageOverlay.UpdateStatus();
            Check(Children(ageOverlay).OfType<TextBlock>().Any(t => t.Text == "Pinned · Not currently observed"), "Expired pin explicitly reports absence instead of remaining a nearby sighting");
            Program.Save(ageOverlay, Path.Combine(folder, "nearby-pin-absent.png"), 340, 170);
            ageOverlay.DisposeOverlay();
        }
        var recentButton = Children(overlay).OfType<Button>().Single(b => Equals(b.Content, "Recent"));
        recentButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump(30);
        Check(host.PetOverlayPreferences.TrackerMode == "Recent" && overlay.DisplayedPetCount == 1, "Recent switch saves mode and shows only recent activity");
        Children(overlay).OfType<Button>().Single(b => Equals(b.Content, "Nearby")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump(30);
        Check(host.PetOverlayPreferences.TrackerMode == "Nearby" && overlay.DisplayedPetCount == 2, "Nearby switch restores observed mobs without another kill");
        var nearbySettings = new SettingsWindow(host);
        ShowOffscreen(nearbySettings);
        nearbySettings.ShowSection("Tracking");
        Pump(30);
        Children(nearbySettings).OfType<Expander>().Single(e => Equals(e.Header, "Sorting and timing")).IsExpanded = true;
        Pump(30);
        var timeoutChoice = Children(nearbySettings).OfType<ComboBox>().Single(c => c.Items.Contains(300));
        timeoutChoice.SelectedItem = 300;
        Pump(400);
        Check(host.PetOverlayPreferences.NearbyStaleSeconds == 300, "Nearby timeout setting saves interactively");
        Program.Save(nearbySettings, Path.Combine(folder, "nearby-settings.png"), 780, 700);
        nearbySettings.Close();
        var arrowSettings = new SettingsWindow(host);
        ShowOffscreen(arrowSettings);
        arrowSettings.ShowSection("Tracking");
        Pump(30);
        Check(!Children(arrowSettings).OfType<CheckBox>().Any(c => Equals(c.Content, "Direction arrows (experimental)")), "Release settings do not advertise unfinished walking navigation");
        Program.Save(arrowSettings, Path.Combine(folder, "arrow-settings.png"), 780, 700);
        arrowSettings.Close();
        host.Preview(view with
        {
            Nearby = Array.Empty<NearbyPet>()
        });
        Pump(30);
        Check(overlay.DisplayedPetCount == 0 && Children(overlay).OfType<TextBlock>().Any(t => t.Text.StartsWith("No pet mobs detected nearby")), "Removal updates the nearby UI immediately");
        Program.Save(overlay, Path.Combine(folder, "nearby-empty.png"), 340, 145);

        // Exercise the condensed settings through actual WPF controls and saved patches.
        var preservedProfile = System.Text.Json.JsonSerializer.Serialize(profile);
        view = view with
        {
            Settings = new()
            {
                TrackerMode = "Recent",
                PinnedPetId = 1050,
                Scale = 1.25,
                InactiveSeconds = 60,
                HideWhenEmpty = true,
                Opacity = .5
            },
            Recent = new(),
            Nearby = Array.Empty<NearbyPet>()
        };
        host.Preview(view);
        var condensed = new SettingsWindow(host);
        ShowOffscreen(condensed);
        Pump(30);
        Check(Children(condensed).OfType<Button>().Any(b => Equals(b.Content, "Tracking") && b.IsVisible) && !Children(condensed).OfType<Button>().Any(b => Equals(b.Content, "Accounts")), "Settings have consolidated navigation without an Accounts page");
        Children(condensed).OfType<Expander>().Single(e => Equals(e.Header, "Sorting and timing")).IsExpanded = true;
        Pump(30);
        var expiry = Children(condensed).OfType<ComboBox>().Single(c => c.Items.Contains("Never"));
        expiry.SelectedItem = "Never";
        Pump(350);
        Check(!host.PetOverlayPreferences.ExpireInactive && host.PetOverlayPreferences.InactiveSeconds == 60, "Never disables expiry without discarding the saved duration");
        expiry.SelectedItem = "30 seconds";
        Pump(350);
        Check(host.PetOverlayPreferences.ExpireInactive && host.PetOverlayPreferences.InactiveSeconds == 30, "Combined expiry control saves enabled state and duration together");
        var modeControl = Children(condensed).OfType<ComboBox>().Single(c => c.Items.Contains("Nearby"));
        modeControl.SelectedItem = "Nearby";
        Pump(350);
        Check(!expiry.IsVisible && Children(condensed).OfType<ComboBox>().Any(c => c.Items.Contains(15) && c.Items.Contains(300) && c.IsVisible), "Nearby shows its observation timeout instead of Recent expiry");
        modeControl.SelectedItem = "Recent";
        Pump(350);
        Check(expiry.IsVisible && Equals(expiry.SelectedItem, "30 seconds"), "Returning to Recent restores its saved timer");
        condensed.ShowSection("Appearance");
        Pump(30);
        Children(condensed).OfType<Button>().Single(b => Equals(b.Content, "Reset this section")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump(350);
        Check(host.PetOverlayPreferences.Scale == 1 && host.PetOverlayPreferences.IsPinned(1050) && host.PetOverlayPreferences.InactiveSeconds == 30, "Section reset preserves unrelated preferences and pinned target");
        Check(System.Text.Json.JsonSerializer.Serialize(profile) == preservedProfile, "Settings edits and reset never alter pet collection data");
        var alpha = Children(condensed).OfType<Slider>().Last();
        alpha.Value = .4;
        Pump(350);
        Check(overlay.Opacity == 1 && Math.Abs(((Border)overlay.Content).Background.Opacity - .4) < .001, "Transparency affects background without fading pet text and portraits");
        condensed.ShowSection("Overlay");
        Children(condensed).OfType<Expander>().Single(e => Equals(e.Header, "More overlay options")).IsExpanded = true;
        Pump(30);
        host.SetHotkeyError("Shortcut unavailable. Choose another combination.");
        condensed.RefreshStatus();
        Pump(30);
        Check(Children(condensed).OfType<TextBlock>().Any(t => t.Text == host.HotkeyError), "Shortcut conflicts are explained within settings");
        condensed.ShowSection("Account");
        Pump(30);
        var share = Children(condensed).OfType<CheckBox>().Single(c => Equals(c.Content, "Share progress across all characters"));
        share.IsChecked = true;
        Pump(350);
        Check(host.PetOverlayPreferences.ShareAllCharacters, "Shared-character preference saves through its real settings control");
        Check(!Children(condensed).OfType<Button>().Any(b => Equals(b.Content, "Reset this section") && b.IsVisible), "Account page has no reset that could discard sharing consent");
        share.BringIntoView();
        Pump(50);
        Program.Save(condensed, Path.Combine(folder, "shared-account-general.png"), 780, 700);
        condensed.ShowConnection();
        Pump(30);
        Check(!Children(condensed).OfType<Button>().Any(b => Equals(b.Content, "Restart capture") && b.IsVisible), "Capture recovery is hidden until advanced troubleshooting is expanded");
        Check(!Children(condensed).OfType<Button>().Any(b => Equals(b.Content, "Download Npcap") && b.IsVisible), "Healthy capture does not advertise driver setup");
        for (int depth = 0; depth < 2; depth++)
        {
            foreach (var ex in Children(condensed).OfType<Expander>().ToArray())
                ex.IsExpanded = true;
            Pump(30);
        }
        Check(Children(condensed).OfType<ComboBox>().Any(c => c.SelectedItem is CaptureAdapterOption a && a.Id == ""), "Adapter override starts with a labelled Automatic choice");
        Check(SettingsWindow.ConnectionAssessment(view with
        {
            Devices = 0
        }, null).Contains("Capture is unavailable") && SettingsWindow.ConnectionAssessment(view with
        {
            Health = null
        }, null).Contains("timing is not available"), "Connection check distinguishes failed capture from missing evidence");
        var oldTraffic = new ConnectionDiagnostics(10, 20, 1, 0, DateTimeOffset.UtcNow.AddMinutes(-1), null, null, "", Array.Empty<DiagnosticEvent>(), null);
        var liveTraffic = view with { Ready = true, Status = "Live", Devices = 1, Health = oldTraffic with { LastPayload = DateTimeOffset.UtcNow } };
        Check(SettingsWindow.ConnectionAssessment(liveTraffic with { CollectionSynced = false }, null).Contains("Collection not synced"), "Receiving traffic cannot claim that the collection is synced");
        Check(SettingsWindow.ConnectionAssessment(liveTraffic with { CollectionSynced = true }, null).Contains("Collection synced"), "Help explicitly confirms collection sync");
        var interruptedView = view with
        {
            Ready = true,
            Devices = 1,
            Status = "Collection sync needed",
            Detail = "Totals are last-known until a fresh collection arrives.",
            Health = oldTraffic
        };
        Check(SettingsWindow.ConnectionAssessment(interruptedView, null).Contains(interruptedView.Detail), "Quiet traffic must not hide the known stale-collection explanation");
        var failedCapture = view with
        {
            Ready = true,
            Devices = 1,
            Status = "Capture unavailable",
            Detail = "Packet capture could not start.",
            Health = null
        };
        Check(SettingsWindow.ConnectionAssessment(failedCapture, null).Contains(failedCapture.Detail), "A retained character identity cannot mask a known capture failure as missing timing");
        var retryView = view with
        {
            Status = "Background retry needed",
            Detail = "Your latest changes could not be saved. Retrying automatically.",
            Health = oldTraffic
        };
        Check(SettingsWindow.ConnectionAssessment(retryView, null).Contains(retryView.Detail), "Save-retry instructions take precedence over quiet traffic");
        Check(SettingsWindow.ConnectionAssessment(view with
        {
            Status = "Live",
            Health = oldTraffic
        }, null).Contains("no recent game traffic"), "Healthy known totals still report an idle traffic interval");
        using (var interruptedHost = new Host(Path.Combine(folder, "interrupted-help"), false))
        {
            interruptedHost.Preview(interruptedView with
            {
                Settings = new()
                {
                    Visible = false
                }
            });
            var helpWindow = new SettingsWindow(interruptedHost);
            ShowOffscreen(helpWindow);
            helpWindow.ShowConnection();
            Pump(30);
            Program.Save(helpWindow, Path.Combine(folder, "interrupted-help.png"), 780, 700);
            helpWindow.Close();
        }
        Program.Save(condensed, Path.Combine(folder, "condensed-help-expanded.png"), 780, 700);
        condensed.Close();
        using (var quiet = new Host(Path.Combine(folder, "quiet-case"), false))
        {
            var emptyView = view with
            {
                Settings = new()
                {
                    HideWhenEmpty = true
                },
                Recent = new(),
                Nearby = Array.Empty<NearbyPet>()
            };
            quiet.Preview(emptyView);
            Check(quiet.Overlay == null, "Hide when empty does not create a visible empty overlay");
            quiet.SetPetOverlayVisible(true);
            Pump(30);
            Check(quiet.Overlay != null, "Explicit tray or shortcut show can reveal an empty tracker");
            quiet.Preview(emptyView with
            {
                Recent = new() { new(1050, null, "", DateTimeOffset.UtcNow) }
            });
            var sameQuiet = quiet.Overlay!;
            ShowOffscreen(sameQuiet);
            Pump(20);
            quiet.Preview(emptyView);
            Check(!sameQuiet.IsVisible, "Empty tracker hides again after new activity has appeared");
            quiet.Preview(emptyView with
            {
                Ready = false,
                Status = "Waiting for character"
            });
            Check(quiet.Overlay != null, "Hide when empty retains connection feedback while waiting for a character");
        }
        // Explicit synthetic guidance exercises rendering only, not live coordinate decoding.
        using (var navigationHost = new Host(Path.Combine(folder, "direction-case"), false))
        {
            var at = DateTimeOffset.UtcNow;
            var navigationView = view with
            {
                Settings = new()
                {
                    TrackerMode = "Nearby",
                    ExpireInactive = false
                },
                Nearby = new[] { new NearbyPet(1050, "Gravi", at, new(12, at, 45, at)), new NearbyPet(1060, "Starturtle", at, new(28, at, 270, at)) }
            };
            navigationHost.Preview(navigationView);
            var navigationOverlay = navigationHost.Overlay!;
            ShowOffscreen(navigationOverlay);
            Pump(30);
            var cues = Children(navigationOverlay).OfType<PetDirectionIndicator>().ToArray();
            Check(cues.Length == 2 && cues.All(c => c.IsVisible), "Per-pet direction indicators render beside their own rows");
            var firstCue = cues.Single(c => System.Windows.Automation.AutomationProperties.GetAutomationId(c) == "PetDirection-1050");
            var arrow = Children(firstCue).OfType<System.Windows.Shapes.Path>().Single();
            Check(((RotateTransform)arrow.RenderTransform).Angle == 45 && Children(firstCue).OfType<TextBlock>().Single().Text == "12 m", "Arrow rotation and distance match supplied guidance");
            Check(System.Windows.Automation.AutomationProperties.GetName(firstCue).Contains("up means ahead"), "Accessible direction describes character-relative orientation");
            Program.Save(navigationOverlay, Path.Combine(folder, "arrow-preview.png"), 340, 245);
            navigationView = navigationView with
            {
                Nearby = new[] { new NearbyPet(1050, "Gravi", at, new(12, at, 45, at.AddSeconds(-3))) }
            };
            navigationHost.Preview(navigationView);
            Pump(20);
            Check(firstCue.IsVisible && arrow.Visibility == Visibility.Collapsed, "Expired heading leaves valid distance without a frozen arrow");
            navigationHost.Preview(navigationView with
            {
                Nearby = new[] { new NearbyPet(1050, "Gravi", at) }
            });
            Pump(20);
            Check(firstCue.Visibility == Visibility.Collapsed, "Missing guidance reserves no empty navigation space");
            at = DateTimeOffset.UtcNow;
            navigationHost.Preview(navigationView with
            {
                Nearby = new[] { new NearbyPet(1050, "Gravi", at, new(.3, at, 0, at)) }
            });
            Pump(20);
            Check(Children(firstCue).OfType<TextBlock>().Single().Text == "<1 m" && arrow.Visibility == Visibility.Visible, "Submetre distance and renewed heading display correctly");
            Pump(2300);
            Check(firstCue.Visibility == Visibility.Collapsed, "UI expires guidance without receiving another worker update");
            at = DateTimeOffset.UtcNow;
            navigationHost.Preview(navigationView with
            {
                Nearby = new[] { new NearbyPet(1050, "Gravi", at, new(12, at, 90, at)) }
            });
            Pump(20);
            navigationHost.Preview(navigationView with
            {
                Ready = false,
                Character = "Other"
            });
            Pump(20);
            Check(!Children(navigationOverlay).OfType<PetDirectionIndicator>().Any(c => c.IsVisible), "Disconnect or character reset removes old guidance");
            at = DateTimeOffset.UtcNow;
            navigationHost.Preview(navigationView with
            {
                Nearby = new[] { new NearbyPet(1050, "Gravi", at, new(null, at, 90, at, SkillBased: true)) }
            });
            Pump(20);
            firstCue = Children(navigationOverlay).OfType<PetDirectionIndicator>().Single(c => System.Windows.Automation.AutomationProperties.GetAutomationId(c) == "PetDirection-1050");
            arrow = Children(firstCue).OfType<System.Windows.Shapes.Path>().Single();
            Check(firstCue.IsVisible && arrow.Visibility == Visibility.Visible && ((RotateTransform)arrow.RenderTransform).Angle == 90 && Children(firstCue).OfType<TextBlock>().Single().Text == "", "Direction-only arrow has no invented distance label");
            Check(System.Windows.Automation.AutomationProperties.GetName(firstCue).Contains("last skill facing"), "Experimental tooltip identifies the actual orientation source");
            Program.Save(navigationOverlay, Path.Combine(folder, "arrow-only-preview.png"), 340, 245);
            at = DateTimeOffset.UtcNow;
            navigationHost.Preview(navigationView with
            {
                Nearby = new[] { new NearbyPet(1050, "Gravi", at, new(null, at, 90, NorthUp: true)) }
            });
            Pump(20);
            Check(firstCue.IsVisible && ((RotateTransform)arrow.RenderTransform).Angle == 90 && Children(firstCue).OfType<TextBlock>().Single().Text == "N up", "Map arrow displays its north-up reference without a distance or heading");
            Check(System.Windows.Automation.AutomationProperties.GetName(firstCue).Contains("up is north"), "Map arrow tooltip explains orientation");
            Program.Save(navigationOverlay, Path.Combine(folder, "compass-arrow-preview.png"), 340, 245);
            Pump(2300);
            Check(firstCue.Visibility == Visibility.Collapsed, "Direction-only arrow expires without another worker update");
        }
        // Ordinary launch must recover an overlay hidden during a previous session.
        using (var launchHost = new Host(Path.Combine(folder, "launch-visibility"), false))
        {
            var launchView = view with
            {
                Ready = true,
                Status = "Live",
                Settings = new()
                {
                    Visible = false,
                    HideWhenEmpty = true,
                    Scale = 1.25,
                    PinnedPetIds = new(),
                    PinLimit = 4
                },
                Recent = new(),
                Nearby = Array.Empty<NearbyPet>(),
                Market = Array.Empty<MarketStock>()
            };
            launchHost.Preview(launchView);
            Check(launchHost.Overlay == null, "Saved hidden state is represented by the startup fixture");
            var savedCollection = System.Text.Json.JsonSerializer.Serialize(launchHost.Tracker.Profile);
            Check(launchHost.InitializeLaunch(false).GetAwaiter().GetResult(), "Normal launch saves its explicit show request");
            Pump(20);
            Check(launchHost.Overlay != null && launchHost.PetOverlayPreferences.Visible, "Normal launch restores an overlay hidden in the last session");
            var launchOverlay = launchHost.Overlay!;
            ShowOffscreen(launchOverlay);
            Pump(20);
            for (int i = 0; i < 3; i++)
                launchHost.Preview(launchHost.View!);
            Check(launchOverlay.IsVisible && launchOverlay.DisplayedPetCount == 0, "Repeated empty status updates do not hide the freshly opened tracker");
            Check(launchHost.PetOverlayPreferences.Scale == 1.25 && launchHost.PetOverlayPreferences.PinLimit == 4 && savedCollection == System.Text.Json.JsonSerializer.Serialize(launchHost.Tracker.Profile), "Launch show preserves unrelated preferences and collection progress");
            Program.Save(launchOverlay, Path.Combine(folder, "launch-empty-visible.png"), 425, 180);
            launchHost.SetPetOverlayVisible(false);
            Pump(20);
            Check(!launchOverlay.IsVisible && !launchHost.PetOverlayPreferences.Visible, "User can still deliberately hide the overlay during this session");
            Check(launchHost.InitializeLaunch(false).GetAwaiter().GetResult(), "Another normal launch restores the explicit show state");
            ShowOffscreen(launchOverlay);
            launchHost.Preview(launchHost.View! with
            {
                Recent = new() { new(1050, null, "", DateTimeOffset.UtcNow) }
            });
            launchHost.Preview(launchHost.View! with
            {
                Recent = new()
            });
            Check(!launchOverlay.IsVisible, "Hide when empty resumes after actual activity has appeared");
        }
        using (var minimizedHost = new Host(Path.Combine(folder, "minimized-launch"), false))
        {
            minimizedHost.StartHidden = true;
            minimizedHost.Preview(view with
            {
                Settings = new()
                {
                    Visible = true
                },
                Recent = new()
            });
            Check(minimizedHost.InitializeLaunch(true).GetAwaiter().GetResult() && minimizedHost.Overlay == null, "Explicit minimized CLI launch remains hidden");
        }
        using (var gameLaunch = new Host(Path.Combine(folder, "game-only-launch"), false))
        {
            gameLaunch.IsGameForeground = _ => false;
            gameLaunch.Preview(view with
            {
                Settings = new()
                {
                    GameOnly = true,
                    Visible = false,
                    HideWhenEmpty = true
                },
                Recent = new(),
                Nearby = Array.Empty<NearbyPet>()
            });
            gameLaunch.InitializeLaunch(false).GetAwaiter().GetResult();
            Check(gameLaunch.Overlay == null, "Launch show respects the user's explicit game-only preference");
            gameLaunch.IsGameForeground = _ => true;
            gameLaunch.Preview(gameLaunch.View!);
            Check(gameLaunch.Overlay != null, "Game-only launch becomes visible when Aion 2 receives focus");
        }
        using (var cleanHost = new Host(Path.Combine(folder, "clean-settings"), false))
        {
            cleanHost.Preview(view with
            {
                Ready = true,
                Status = "Live",
                CollectionSynced = true,
                Settings = new(),
                Health = new ConnectionDiagnostics(10, 20, 1, 0, DateTimeOffset.UtcNow, null, null, "", Array.Empty<DiagnosticEvent>(), null)
            });
            var clean = new SettingsWindow(cleanHost);
            ShowOffscreen(clean);
            Pump(40);
            foreach (var section in new[] { "Tracking", "Overlay", "Account", "Updates", "Help" })
            {
                clean.ShowSection(section);
                Pump(30);
                Program.Save(clean, Path.Combine(folder, "clean-" + section.ToLowerInvariant() + ".png"), 820, 740);
            }
            clean.ShowSection("Tracking");
            Pump(20);
            Check(!Children(clean).OfType<CheckBox>().Any(c => new[] { "Start with Windows", "Start with tracker hidden", "Show portraits", "Show progress bars", "Compact width" }.Contains(c.Content?.ToString())), "Retired startup and cosmetic switches are absent from settings");
            Check(!Children(clean).OfType<ComboBox>().Any(c => System.Windows.Automation.AutomationProperties.GetAutomationId(c) == "Accent"), "Decorative color picker is removed");
            Check(Children(clean).OfType<CheckBox>().Any(c => Equals(c.Content, "Show merchant shopping list in overlay") && c.IsVisible), "Merchant recommendations live on Tracking");
            Check(Children(clean).OfType<Expander>().Single(e => Equals(e.Header, "Sorting and timing")).IsExpanded == false, "Timing details start collapsed");
            clean.Width = 720;
            clean.Height = 560;
            Pump(40);
            foreach (var section in new[] { "Tracking", "Overlay", "Account", "Updates", "Help" })
            {
                clean.ShowSection(section);
                Pump(25);
                Program.Save(clean, Path.Combine(folder, "small-" + section.ToLowerInvariant() + ".png"), 720, 560);
                Check(Children(clean).OfType<Button>().Single(b => Equals(b.Content, "Done")).IsVisible, "Done remains reachable on the small " + section + " page");
            }
            clean.Close();
        }
        SettingsSaveChecks.Run(Check, folder, view, Pump, ShowOffscreen);
        DirectionPollingChecks.Run(Check, folder, view, Pump, ShowOffscreen);

        SettingsSaveChecks.PipeCompatibility(Check, folder, view, Pump);
        SettingsSaveChecks.RecordedLegacyPipe(Check, folder, view, Pump);
        var families = new Dictionary<string, string> { { "Intellect", "Cogni" }, { "Feral", "Fera" }, { "Nature", "Natura" }, { "Trans", "Varian" }, { "Special", "Special" } };
        Check(Catalog.Entries.All(p => families.TryGetValue(p.Category, out var label) && PetFamilyBadge.Label(p.Id) == label), "Every catalog pet resolves its verified family name without altering saved categories");
        var familyStrip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10) };
        foreach (var family in families)
        {
            int id = Catalog.Entries.First(p => p.Category == family.Key).Id;
            var badge = new PetFamilyBadge(id);
            familyStrip.Children.Add(badge);
            Check(Children(badge).OfType<Image>().Single().Source is System.Windows.Media.Imaging.BitmapSource bitmap && bitmap.PixelWidth == 64 && bitmap.PixelHeight == 64, "Embedded original crystal loads for " + family.Value);
            Check(Children(badge).OfType<TextBlock>().Single().Text == family.Value, "Crystal tag labels " + family.Value);
        }
        Check(new PetFamilyBadge(-1).Visibility == Visibility.Collapsed, "Unknown pet category never receives a guessed crystal tag");
        var familyWindow = new Window { Content = familyStrip, Width = 440, Height = 90, Background = Ui.Dark, WindowStyle = WindowStyle.None };
        ShowOffscreen(familyWindow);
        Pump(20);
        Program.Save(familyWindow, Path.Combine(folder, "family-crystals.png"), 440, 90);
        familyWindow.Close();
        var quietTracker = new Tracker("", Catalog.Create());
        quietTracker.ReadOnlyView(view with
        {
            Ready = true,
            Recent = new() { new(1050, null, "", DateTimeOffset.UtcNow.AddMinutes(-1)) }
        });
        Check(Activity.EmptyText(quietTracker, new AppSettings { TrackerMode = "Recent", ExpireInactive = true, InactiveSeconds = 20 }, DateTimeOffset.UtcNow, view) == "", "Expired Recent activity has no timeout chatter");
        PlacementChecks.Run(Check);
        var quitChecks = QuitChecks.Run(Check, folder, view).WaitAsync(TimeSpan.FromSeconds(15));
        while (!quitChecks.IsCompleted)
            Pump(10);
        quitChecks.GetAwaiter().GetResult();
        Console.WriteLine($"PASS: {passed} WPF checks");
        host.Dispose();
        app.Shutdown();

    }

}



sealed class UpdatePreviewClient : PetTracker.Updates.IUpdateClient
{

    public System.Threading.Tasks.Task<Velopack.UpdateInfo?> Check()
    {
        var asset = Velopack.VelopackAssetFeed.FromJson("{\"Assets\":[{\"PackageId\":\"PetTracker.Desktop\",\"Version\":\"0.2.0\",\"Type\":\"Full\",\"FileName\":\"preview.nupkg\",\"SHA256\":\"00\",\"Size\":1}]}").Assets.Single();
        return System.Threading.Tasks.Task.FromResult<Velopack.UpdateInfo?>(new Velopack.UpdateInfo(asset, false, null, Array.Empty<Velopack.VelopackAsset>()));
    }

    public System.Threading.Tasks.Task Download(Velopack.UpdateInfo update, Action<int> progress, System.Threading.CancellationToken cancellation) => System.Threading.Tasks.Task.CompletedTask;

    public void VerifyReady(Velopack.UpdateInfo update)
    {
    }

    public void Apply(Velopack.UpdateInfo update, string[] arguments) => throw new InvalidOperationException("Preview cannot install updates");

    public void Dispose()
    {
    }

}
