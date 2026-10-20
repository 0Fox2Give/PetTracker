using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace PetTracker;

public sealed class PetTrackerOverlay : Window
{
    readonly Host host;
    readonly Border shell;
    readonly PetNames names;
    readonly PetPortraits portraits;
    readonly StackPanel rows = new();
    readonly StackPanel merchantRows = new();
    readonly Border merchantSection;
    readonly TextBlock merchantHint = new()
    {
        Text = "Pets only from Chests or Black Cloud Merchants",
        Foreground = Ui.Muted,
        FontSize = 10,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 3, 0, 4)
    };
    string merchantKey = "";
    public int DisplayedMerchantCount { get; private set; }

    readonly CheckBox hideMax;
    readonly Dictionary<string, Button> modes = new();
    readonly Dictionary<int, Row> cards = new();
    readonly Func<DateTimeOffset> now;
    readonly bool animate;
    readonly System.Windows.Threading.DispatcherTimer directionTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    bool allowClose, dragging, disposed, syncingFilter;
    int[] displayedIds = Array.Empty<int>();
    DateTimeOffset? noticeExpires;
    public int DisplayedPetCount { get; private set; }

    public PetTrackerOverlay(Host host, Func<DateTimeOffset>? clock = null, bool animate = true)
    {
        this.host = host;
        now = clock ?? (() => DateTimeOffset.UtcNow);
        this.animate = animate;
        names = new();
        portraits = new(host.AssetRoot);
        Title = "Pet Tracker";
        Width = 320;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        if (host.PreviewOnly)
        {
            Topmost = false;
            Left = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth + 2048;
            Top = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight + 2048;
        }

        Resources.MergedDictionaries.Add(NativeTheme.Resources());
        FontFamily = SystemFonts.MessageFontFamily;
        FontSize = 11;
        Brush Brush(string key) => (Brush)FindResource(key);
        shell = new Border
        {
            Background = Brush("ArcanaDark"),
            BorderBrush = new SolidColorBrush(Color.FromRgb(45, 51, 61)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8),
            Margin = new Thickness(5),
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 10,
                ShadowDepth = 2,
                Opacity = .24
            }
        };
        var layout = new StackPanel();
        shell.Child = layout;
        Content = shell;
        var header = new DockPanel { Height = 32, LastChildFill = true, Cursor = Cursors.SizeAll };
        var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(controls, Dock.Right);
        header.Children.Add(controls);
        Button Action(string glyph, string tip, Action click)
        {
            var button = new Button
            {
                Style = (Style)FindResource("PetTrackerChromeButton"),
                Width = 22,
                Height = 22,
                Content = glyph,
                ToolTip = new ToolTip
                {
                    Style = (Style)FindResource("ArcanaToolTipStyle"),
                    Content = tip
                }
            };
            button.Click += (s, e) =>
            {
                e.Handled = true;
                click();
            };
            return button;
        }

        controls.Children.Add(Action("\u2315", "Find a pet or mob", () => host.OpenPetFinder()));
        controls.Children.Add(Action("\u2699", "Tracker settings", host.OpenPetOverlaySettings));
        controls.Children.Add(Action("\u00D7", "Hide pet tracker", () => host.SetPetOverlayVisible(false)));
        hideMax = new CheckBox
        {
            Content = "Hide maxed",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(153, 163, 177)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 6, 0),
            IsChecked = host.PetOverlayPreferences.HideMaxed
        };
        DockPanel.SetDock(hideMax, Dock.Right);
        header.Children.Add(hideMax);
        void Filter(object? sender, RoutedEventArgs e)
        {
            if (syncingFilter)
                return;
            var settings = host.PetOverlayPreferences;
            host.SetPetOverlayOptions(hideMax.IsChecked == true, settings.ExpireInactive, settings.InactiveSeconds);
            Refresh();
        }

        hideMax.Checked += Filter;
        hideMax.Unchecked += Filter;
        header.Children.Add(new TextBlock { Text = "Pet Tracker", Foreground = new SolidColorBrush(Color.FromRgb(222, 229, 239)), FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        header.MouseLeftButtonDown += (s, e) =>
        {
            if (e.OriginalSource is DependencyObject target && InsideControl(target))
                return;
            if (!host.PetOverlayPreferences.Locked && e.LeftButton == MouseButtonState.Pressed)
                try
                {
                    dragging = true;
                    DragMove();
                    ConstrainToDesktop();
                    host.SavePetOverlayPosition(Left, Top);
                    e.Handled = true;
                }
                catch (InvalidOperationException)
                {
                }
                finally
                {
                    dragging = false;
                }
        };
        layout.Children.Add(new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(41, 47, 56)), BorderThickness = new Thickness(0, 0, 0, 1), Child = header });
        var modeBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 2) };
        foreach (string mode in new[] { "Recent", "Nearby" })
        {
            var button = Ui.Button(mode, async () => await host.Change(new("patch", Patch: SettingsPatch.Fields((nameof(AppSettings.TrackerMode), mode)))));
            button.Height = 24;
            button.MinHeight = 0;
            button.Width = 70;
            button.Padding = new Thickness(6, 2, 6, 2);
            button.Margin = new Thickness(0, 0, 4, 0);
            button.FontSize = 11;
            button.ToolTip = mode == "Nearby" ? "Pet mobs observed by the game client. Distance is not measured." : "Your recent pet rewards and tracked kills.";
            System.Windows.Automation.AutomationProperties.SetAutomationId(button, "TrackerMode-" + mode);
            modes[mode] = button;
            modeBar.Children.Add(button);
        }

        var merchant = new StackPanel();
        merchant.Children.Add(new TextBlock { Text = "Black Cloud Merchant", Foreground = Ui.Ink, FontSize = 12, FontWeight = FontWeights.SemiBold });
        merchant.Children.Add(merchantHint);
        merchant.Children.Add(new ScrollViewer { Content = merchantRows, MaxHeight = 240, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        merchantSection = new Border
        {
            Child = merchant,
            Visibility = Visibility.Collapsed,
            Padding = new Thickness(4, 8, 4, 8),
            BorderBrush = new SolidColorBrush(Color.FromRgb(41, 47, 56)),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(merchantSection, "MerchantRecommendations");
        layout.Children.Add(merchantSection);
        layout.Children.Add(modeBar);
        layout.Children.Add(rows);
        Closing += (s, e) =>
        {
            if (!allowClose)
            {
                e.Cancel = true;
                host.SetPetOverlayVisible(false);
            }
        };
        SizeChanged += (s, e) =>
        {
            if (IsVisible && !dragging)
                ConstrainToDesktop();
        };
        directionTimer.Tick += (s, e) => RefreshDirections(now());
        IsVisibleChanged += (s, e) =>
        {
            if (IsVisible)
                RefreshDirections(now());
            else
                directionTimer.Stop();
        };
        Refresh();
    }

    public void SetBackgroundOpacity(double value)
    {
        var brush = ((Brush)FindResource("ArcanaDark")).Clone();
        brush.Opacity = value;
        shell.Background = brush;
    }

    public void Refresh()
    {
        if (disposed)
            return;
        lock (host.Gate)
        {
            var tracker = host.Tracker;
            var settings = host.PetOverlayPreferences;
            var time = now();
            RefreshMerchant(time);
            foreach (var mode in modes)
            {
                bool selected = mode.Key == settings.TrackerMode;
                mode.Value.Background = selected ? NativeTheme.Accent(settings.Accent) : Brushes.Transparent;
                mode.Value.Foreground = selected ? Brushes.Black : Ui.Muted;
            }

            var activities = tracker.Ready ? PetHudActivityPolicy.Select(tracker, settings, time).ToArray() : Array.Empty<RecentPetActivity>();
            var ids = activities.Select(a => a.PetId!.Value).ToHashSet();
            displayedIds = activities.Select(a => a.PetId!.Value).ToArray();
            foreach (int id in cards.Keys.Where(id => !ids.Contains(id)).ToArray())
            {
                cards[id].Cancel();
                cards.Remove(id);
            }

            var desiredRows = new List<UIElement>();
            DisplayedPetCount = 0;
            noticeExpires = null;
            syncingFilter = true;
            try
            {
                hideMax.IsChecked = settings.HideMaxed;
            }
            finally
            {
                syncingFilter = false;
            }

            foreach (var activity in activities)
            {
                var pet = tracker.Profile.Pets[activity.PetId!.Value];
                if (!cards.TryGetValue(pet.Id, out var row))
                {
                    row = CreateRow(pet.Id);
                    cards.Add(pet.Id, row);
                }

                row.Activity = activity;
                row.Picture.Visibility = settings.Portraits ? Visibility.Visible : Visibility.Collapsed;
                row.Picture.Source = portraits.Get(pet.Id);
                row.Name.Text = names.Resolve(pet);
                row.Name.Foreground = (Brush)FindResource(pet.Max ? "ArcanaTextDim" : "ArcanaTextNeutral");
                row.Rank.Text = pet.Level == 0 ? "Unranked" : "Rank " + (pet.Level?.ToString() ?? "?");
                row.Progress.Text = pet.Max ? "\u2713 MAX" : (pet.CurrentSouls?.ToString() ?? "?") + " / " + (pet.Goal?.ToString() ?? "?");
                row.Badge.Background = pet.Max ? new SolidColorBrush(Color.FromRgb(25, 32, 42)) : Brushes.Transparent;
                row.Progress.Foreground = new SolidColorBrush(pet.Max ? Color.FromRgb(145, 155, 170) : Color.FromRgb(220, 226, 235));
                double ratio = pet.Goal is> 0 && pet.CurrentSouls is int points ? Math.Clamp((double)points / pet.Goal.Value, 0, 1) : 0;
                row.Bar.Visibility = pet.Max || !settings.ProgressBars ? Visibility.Hidden : Visibility.Visible;
                ((Border)row.Bar.Children[0]).Background = NativeTheme.Accent(settings.Accent);
                row.Bar.ColumnDefinitions[0].Width = new GridLength(ratio, GridUnitType.Star);
                row.Bar.ColumnDefinitions[1].Width = new GridLength(1 - ratio, GridUnitType.Star);
                string notice = pet.RankIncreasePending ? "Rank increase pending" : settings.IsPinned(pet.Id) ? "Pinned" : "";
                if (!pet.Max && tracker.LastRankIncrease is { } increase && increase.PetId == pet.Id && time - increase.SeenUtc < TimeSpan.FromSeconds(20))
                {
                    notice = "Rank increased";
                    noticeExpires = increase.SeenUtc.AddSeconds(20);
                }

                string observation = Activity.ObservationNotice(activity, settings, time);
                if (observation.Length > 0)
                    notice = notice.Length > 0 ? notice + " · " + observation : observation;
                row.Notice.Text = notice;
                row.Notice.Visibility = notice.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
                row.Button.ToolTip = new ToolTip
                {
                    Style = (Style)FindResource("ArcanaToolTipStyle"),
                    Content = new TextBlock
                    {
                        MaxWidth = 260,
                        TextWrapping = TextWrapping.Wrap,
                        Text = (activity.CreatureName.Length > 0 ? (activity.IsNearby ? "Observed mobs: " : "Last creature: ") + activity.CreatureName + "\n" : "") + pet.LevelSourceDescription + "\n" + TrackingGuidance.Remaining(pet) + "\n" + (pet.ProgressNeedsSync && !pet.Max ? "Last known soul total. See Settings → Help." : pet.EstimatedSouls.HasValue ? "Soul progress is estimated." : "")
                    }
                };
                ApplyLifetime(row, settings, time);
                desiredRows.Add(row.Button);
                if (PetHudActivityPolicy.Appearance(activity, settings, time).Opacity > 0)
                    DisplayedPetCount++;
            }

            string empty = Activity.EmptyText(tracker, settings, time, host.View);
            if (activities.Length == 0 && DisplayedMerchantCount == 0 && !string.IsNullOrWhiteSpace(empty))
                desiredRows.Add(new TextBlock { Text = empty, ToolTip = !tracker.Ready ? new ToolTip { Style = (Style)FindResource("ArcanaToolTipStyle"), Content = new TextBlock { Text = host.View?.Detail, MaxWidth = 280, TextWrapping = TextWrapping.Wrap } } : null, Foreground = (Brush)FindResource("ArcanaTextDim"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 16, 4, 12) });
            // Keep unchanged controls attached, preserving hover, layout and animations.
            for (int i = rows.Children.Count - 1; i >= 0; i--)
                if (!desiredRows.Contains(rows.Children[i]))
                    rows.Children.RemoveAt(i);
            for (int i = 0; i < desiredRows.Count; i++)
            {
                var child = desiredRows[i];
                if (i < rows.Children.Count && ReferenceEquals(rows.Children[i], child))
                    continue;
                if (rows.Children.Contains(child))
                    rows.Children.Remove(child);
                rows.Children.Insert(i, child);
            }

            RefreshDirections(time);
        }
    }

    void RefreshMerchant(DateTimeOffset time)
    {
        var offers = host.MerchantDisplay.Select(host.View, time);
        DisplayedMerchantCount = offers.Count;
        merchantSection.Visibility = offers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        merchantHint.Visibility = offers.Any(r => !r.Updated) ? Visibility.Visible : Visibility.Collapsed;
        string key = string.Join("|", offers.Select(r =>
        {
            var p = host.View!.Profile.Pets[r.Offer.PetId];
            return $"{r.Offer.PetId}:{r.Offer.Price}:{r.Stock.Tab}:{r.Updated}:{p.Level}:{p.ProgressText}:{names.Resolve(p)}";
        })) + $"/{host.PetOverlayPreferences.Portraits}";
        if (key == merchantKey)
            return;
        merchantKey = key;
        merchantRows.Children.Clear();
        foreach (var offer in offers)
        {
            int id = offer.Offer.PetId;
            var pet = host.View!.Profile.Pets[id];
            var grid = new Grid { Margin = new Thickness(0, 5, 0, 5) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            if (host.PetOverlayPreferences.Portraits)
                grid.Children.Add(new Image { Source = portraits.Get(id), Width = 36, Height = 36, Margin = new Thickness(0, 0, 8, 0), Stretch = Stretch.Uniform });
            var text = new StackPanel();
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            text.Children.Add(new TextBlock { Text = names.Resolve(pet), Foreground = Ui.Ink, FontSize = 12, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            var progressLine = new WrapPanel { Margin = new Thickness(0, 3, 0, 0) };
            progressLine.Children.Add(new TextBlock { Text = $"{(pet.Level == 0 ? "Unranked" : "Rank " + pet.Level)} · {pet.ProgressText}{(pet.Max ? "" : " souls")}", Foreground = Ui.Ink, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
            progressLine.Children.Add(new PetFamilyBadge(id));
            text.Children.Add(progressLine);
            var meta = new DockPanel { Margin = new Thickness(0, 3, 0, 0) };
            var price = new TextBlock
            {
                Text = $"{offer.Offer.Price:N0} Kina",
                Foreground = Ui.Ink,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold
            };
            DockPanel.SetDock(price, Dock.Right);
            meta.Children.Add(price);
            meta.Children.Add(new TextBlock { Text = offer.Stock.TabName, Foreground = Ui.Muted, FontSize = 10 });
            if (!offer.Updated)
                text.Children.Add(meta);
            grid.ToolTip = TrackingGuidance.Remaining(pet) + "\n" + (offer.Updated ? "Soul progress received after this offer was shown. This entry will leave the shopping list shortly." : "Chest and merchant pet. Confirm price and availability in game before purchasing.");
            System.Windows.Automation.AutomationProperties.SetAutomationId(grid, "MerchantPet-" + id);
            merchantRows.Children.Add(grid);
        }
    }

    void RefreshDirections(DateTimeOffset time)
    {
        bool visible = false;
        foreach (var pair in cards)
        {
            var direction = host.Tracker.Ready ? host.Tracker.NearbyPets.FirstOrDefault(p => p.PetId == pair.Key)?.Direction : null;
            pair.Value.Direction.Update(direction, time, NativeTheme.Accent(host.PetOverlayPreferences.Accent));
            visible |= pair.Value.Direction.Visibility == Visibility.Visible;
        }

        if (visible && IsVisible)
            directionTimer.Start();
        else
            directionTimer.Stop();
    }

    Row CreateRow(int id)
    {
        var row = new Row();
        row.Button = new ContentControl
        {
            Style = (Style)FindResource("PetTrackerRow"),
            Height = PetHudActivityPolicy.RowHeight,
            ClipToBounds = true,
            Focusable = false
        };
        var menu = new ContextMenu();
        var find = new MenuItem { Header = "Find on map…" };
        find.Click += (s, e) => host.OpenPetFinder(id);
        menu.Items.Add(find);
        var pin = new MenuItem();
        pin.Click += async (s, e) => await host.PinPet(id, !host.PetOverlayPreferences.IsPinned(id));
        menu.Items.Add(pin);
        menu.Opened += (s, e) =>
        {
            var prefs = host.PetOverlayPreferences;
            pin.Header = prefs.IsPinned(id) ? "Unpin pet" : prefs.CanPin(id) ? "Pin this pet" : "Pin limit reached";
            pin.IsEnabled = prefs.CanPin(id);
            pin.ToolTip = PetPins.Hint(prefs, id);
            ToolTipService.SetShowOnDisabled(pin, true);
        };
        row.Button.ContextMenu = menu;
        System.Windows.Automation.AutomationProperties.SetAutomationId(row.Button, "PetRow-" + id);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Direction = new PetDirectionIndicator();
        Grid.SetColumn(row.Direction, 2);
        grid.Children.Add(row.Direction);
        System.Windows.Automation.AutomationProperties.SetAutomationId(row.Direction, "PetDirection-" + id);
        row.Picture = new Image { Width = 48, Height = 48, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(row.Picture);
        var text = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        row.Name = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        text.Children.Add(row.Name);
        var meta = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 3, 0, 0) };
        row.Progress = new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold };
        row.Badge = new Border { CornerRadius = new CornerRadius(3), Padding = new Thickness(4, 0, 4, 0), Child = row.Progress };
        DockPanel.SetDock(row.Badge, Dock.Right);
        meta.Children.Add(row.Badge);
        row.Rank = new TextBlock
        {
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(133, 144, 160)),
            VerticalAlignment = VerticalAlignment.Center
        };
        var rankAndFamily = new StackPanel { Orientation = Orientation.Horizontal };
        rankAndFamily.Children.Add(row.Rank);
        rankAndFamily.Children.Add(new PetFamilyBadge(id));
        meta.Children.Add(rankAndFamily);
        text.Children.Add(meta);
        row.Bar = new Grid
        {
            Height = 3,
            Margin = new Thickness(0, 7, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(30, 39, 52))
        };
        row.Bar.ColumnDefinitions.Add(new ColumnDefinition());
        row.Bar.ColumnDefinitions.Add(new ColumnDefinition());
        row.Bar.Children.Add(new Border { Background = (Brush)FindResource("ArcanaGold") });
        text.Children.Add(row.Bar);
        row.Notice = new TextBlock
        {
            FontSize = 9,
            Foreground = (Brush)FindResource("ArcanaTextDim"),
            Margin = new Thickness(0, 3, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        text.Children.Add(row.Notice);
        row.Button.Content = grid;
        return row;
    }

    void ApplyLifetime(Row row, AppSettings settings, DateTimeOffset time)
    {
        var activity = row.Activity!;
        var appearance = PetHudActivityPolicy.Appearance(activity, settings, time);
        if (activity.IsNearby || settings.IsPinned(activity.PetId))
        {
            row.Cancel();
            row.Button.Opacity = 1;
            row.Button.Height = PetHudActivityPolicy.RowHeight;
            return;
        }

        if (!animate || settings.ReducedMotion)
        {
            row.Cancel();
            row.Button.Opacity = appearance.Opacity;
            row.Button.Height = appearance.Height;
            return;
        }

        var key = (activity.SeenUtc, settings.ExpireInactive, settings.InactiveSeconds);
        if (row.Lifetime == key)
            return;
        row.Cancel();
        row.Lifetime = key;
        row.Button.Opacity = appearance.Opacity;
        row.Button.Height = appearance.Height;
        if (!settings.ExpireInactive)
            return;
        double remaining = settings.InactiveSeconds - PetHudActivityPolicy.Age(activity, time);
        int generation = row.Generation;
        var fade = new DoubleAnimation
        {
            From = appearance.Opacity,
            To = 0,
            BeginTime = TimeSpan.FromSeconds(Math.Max(0, remaining - PetHudActivityPolicy.FadeSeconds)),
            Duration = TimeSpan.FromSeconds(Math.Max(.001, Math.Min(PetHudActivityPolicy.FadeSeconds, remaining))),
            FillBehavior = FillBehavior.HoldEnd
        };
        row.Button.BeginAnimation(OpacityProperty, fade);
        var collapse = new DoubleAnimation
        {
            From = appearance.Height,
            To = 0,
            BeginTime = TimeSpan.FromSeconds(Math.Max(0, remaining)),
            Duration = TimeSpan.FromSeconds(PetHudActivityPolicy.CollapseSeconds),
            EasingFunction = new CubicEase
            {
                EasingMode = EasingMode.EaseInOut
            },
            FillBehavior = FillBehavior.HoldEnd
        };
        collapse.Completed += (s, e) =>
        {
            if (!disposed && generation == row.Generation)
                Refresh();
        };
        row.Button.BeginAnimation(HeightProperty, collapse);
    }

    public void UpdateStatus()
    {
        if (disposed)
            return;
        lock (host.Gate)
        {
            var time = now();
            var settings = host.PetOverlayPreferences;
            var activities = host.Tracker.Ready ? PetHudActivityPolicy.Select(host.Tracker, settings, time).ToArray() : Array.Empty<RecentPetActivity>();
            if (DisplayedMerchantCount > 0 || settings.TrackerMode == "Nearby" || !animate || !displayedIds.SequenceEqual(activities.Select(a => a.PetId!.Value)) || noticeExpires is { } end && time >= end)
                Refresh();
            else
                DisplayedPetCount = activities.Count(a => PetHudActivityPolicy.Appearance(a, settings, time).Opacity > 0);
        }
    }

    public void DisposeOverlay()
    {
        disposed = true;
        directionTimer.Stop();
        foreach (var row in cards.Values)
            row.Cancel();
        cards.Clear();
        allowClose = true;
        Close();
    }

    void ConstrainToDesktop()
    {
        if (host.PreviewOnly)
            return;
        var point = OverlayGeometry.ClampToScreens(new Point(Left, Top), OverlayGeometry.WorkAreas(this), ActualWidth, ActualHeight);
        if (Left != point.X)
            Left = point.X;
        if (Top != point.Y)
            Top = point.Y;
    }

    static bool InsideControl(DependencyObject node)
    {
        for (DependencyObject? p = node; p != null; p = p is Visual ? VisualTreeHelper.GetParent(p) : LogicalTreeHelper.GetParent(p))
            if (p is Button or CheckBox or ComboBox)
                return true;
        return false;
    }

    sealed class Row
    {
        public ContentControl Button = null!;
        public Image Picture = null!;
        public TextBlock Name = null!, Rank = null!, Progress = null!, Notice = null!;
        public Border Badge = null!;
        public Grid Bar = null!;
        public PetDirectionIndicator Direction = null!;
        public RecentPetActivity? Activity;
        public (DateTimeOffset, bool, int)? Lifetime;
        public int Generation;
        public void Cancel()
        {
            Generation++;
            Button.BeginAnimation(OpacityProperty, null);
            Button.BeginAnimation(HeightProperty, null);
            Lifetime = null;
        }
    }
}
