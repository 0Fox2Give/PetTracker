using System.Diagnostics;
using System.ComponentModel;
using System.Windows.Markup;

namespace PetTracker;

public sealed class PetFinderWindow : Window
{
    bool detailRendered;
    int? renderedPet;
    string renderedPins = "";
    DateOnly renderedDate;
    string renderedMarket = "";
    bool changingFilters;
    readonly CollectionOverview overview = new();
    readonly TextBlock collectionStatus = Ui.Text("", 11, Ui.Muted);
    readonly ComboBox rankFilter = new() { ItemsSource = new[] { "All ranks", "Not collected", "Rank 1", "Rank 2", "Maxed", "Unknown rank" }, SelectedIndex = 0 };
    readonly ComboBox regionFilter = new() { ItemsSource = new[] { "All regions" }.Concat(PetFinder.Entries.SelectMany(e => e.AvailableMaps).Select(m => m.Region).Distinct().Order()).ToArray(), SelectedIndex = 0 };
    readonly ComboBox sort = new() { ItemsSource = new[] { "Name A–Z", "Closest to next rank" }, SelectedIndex = 0 };
    readonly Button clearFilters = Ui.Button("Clear filters", () => { });
    readonly ComboBox filter = new()
    {
        ItemsSource = new[]
        {
            "All sources",
            "Black Market watchlist",
            "Seen in Black Market"
        },
        SelectedIndex = 0,
        MinWidth = 200
    };
    readonly TextBlock marketHelp = Ui.Text("", 11, Ui.Muted);
    readonly Host host;
    readonly TextBox search = new();
    readonly ListBox results = new();
    readonly StackPanel detail = new();
    readonly TextBlock count = Ui.Text("", 11, Ui.Muted);
    readonly PetPortraits portraits;
    sealed class SearchRow(PetSearchResult result, ImageSource? portrait) : INotifyPropertyChanged
    {
        public PetSearchResult Result { get; } = result;
        public ImageSource? Portrait { get; } = portrait;
        public string Label => Result.Label;
        public string Rank { get; private set; } = "Rank ?";
        public string Progress { get; private set; } = "? / ?";
        public string Hint { get; private set; } = "";
        public Brush ProgressBrush { get; private set; } = Ui.Ink;
        public double ProgressValue { get; private set; }
        public Visibility ProgressVisibility { get; private set; } = Visibility.Collapsed;
        public string Remaining { get; private set; } = "";

        public event PropertyChangedEventHandler? PropertyChanged;
        public void Update(Profile profile, bool collectionSynced)
        {
            profile.Pets.TryGetValue(Result.Pet.Id, out var pet);
            string rank = pet?.Level is int level ? (level == 0 ? "Unranked" : "Rank " + level) : "Rank ?";
            string progress = pet?.Max == true ? "\u2713 MAX" : pet?.ProgressText.Replace("/", " / ") ?? "? / ?";
            string hint = Label + " · " + rank + " · " + progress + (pet?.ProgressNeedsSync == true && !pet.Max ? "\nLast known progress; waiting for a fresh collection." : "");
            if (TrackingGuidance.Remaining(pet) is { Length: > 0 } remaining)
                hint += "\n" + remaining;
            Brush brush = pet?.Max == true ? Ui.Muted : Ui.Ink;
            string left = collectionSynced && PetCollection.SoulsRemaining(pet) is int needed ? $"{needed} soul{(needed == 1 ? "" : "s")} to {(pet!.Level == 0 ? "unlock" : "Rank " + (pet.Level + 1))}" : "";
            ProgressValue = pet?.Max == true ? 100 : pet?.CurrentSouls is int souls && pet.Goal is> 0 ? Math.Clamp(100.0 * souls / pet.Goal.Value, 0, 100) : 0;
            ProgressVisibility = pet?.Max == true || pet?.CurrentSouls != null && pet.Goal is> 0 ? Visibility.Visible : Visibility.Collapsed;
            if ((Rank, Progress, Hint, ProgressBrush, Remaining) == (rank, progress, hint, brush, left))
                return;
            Rank = rank;
            Progress = progress;
            Hint = hint;
            ProgressBrush = brush;
            Remaining = left;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        }
    }

    public PetFinderWindow(Host host, int? petId = null)
    {
        this.host = host;
        portraits = new(host.AssetRoot);
        Title = "Pet Tracker — Find a pet";
        Width = 1000;
        Height = 740;
        MinWidth = 720;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        NativeTheme.Install(this);
        NativeTheme.DarkTitleBar(this);
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/PetTracker;component/FinderStyles.xaml", UriKind.Relative) });
        Resources[SystemParameters.VerticalScrollBarWidthKey] = 10.0;
        var layout = new DockPanel { Margin = new Thickness(22) };
        Content = layout;
        var header = new StackPanel();
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);
        var title = Ui.Text("Find a pet", 24);
        title.FontWeight = FontWeights.SemiBold;
        header.Children.Add(title);
        collectionStatus.Margin = new Thickness(0, 5, 0, 14);
        collectionStatus.ToolTip = "Counts cover supported pets in this account. Unknown ranks are not counted as missing pets.";
        System.Windows.Automation.AutomationProperties.SetAutomationId(collectionStatus, "CollectionStatus");
        header.Children.Add(collectionStatus);
        header.Children.Add(overview);
        var toolbar = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        toolbar.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 400 });
        toolbar.ColumnDefinitions.Add(new() { Width = new GridLength(212) });
        search.Tag = "Search pets or mobs…";
        search.Height = 32;
        search.VerticalContentAlignment = VerticalAlignment.Center;
        search.Padding = new Thickness(8, 2, 8, 2);
        search.Margin = new Thickness(0, 0, 12, 0);
        System.Windows.Automation.AutomationProperties.SetAutomationId(search, "PetFinderSearch");
        toolbar.Children.Add(search);
        filter.Margin = new Thickness(0);
        filter.Height = 32;
        Grid.SetColumn(filter, 1);
        toolbar.Children.Add(filter);
        header.Children.Add(toolbar);
        var refinements = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        void AddFilter(ComboBox control, string id, string label, double width)
        {
            control.Width = width;
            control.Height = 32;
            control.Margin = new Thickness(0, 0, 8, 4);
            control.ToolTip = label;
            System.Windows.Automation.AutomationProperties.SetAutomationId(control, id);
            System.Windows.Automation.AutomationProperties.SetName(control, label);
            refinements.Children.Add(control);
        }
        AddFilter(rankFilter, "CollectionRank", "Filter by collection rank", 128);
        AddFilter(regionFilter, "CollectionRegion", "Filter by farming region", 178);
        AddFilter(sort, "CollectionSort", "Sort by name or souls remaining to the next rank. Unknown, estimated and stale balances stay below verified totals.", 182);
        clearFilters.Height = 32;
        clearFilters.Margin = new Thickness(0, 0, 0, 4);
        clearFilters.Padding = new Thickness(10, 2, 10, 2);
        clearFilters.Click += (_, _) => ResetFilters();
        refinements.Children.Add(clearFilters);
        header.Children.Add(refinements);
        header.Children.Add(marketHelp);
        marketHelp.TextWrapping = TextWrapping.Wrap;
        System.Windows.Automation.AutomationProperties.SetAutomationId(filter, "PetFinderFilter");
        count.ToolTip = "Shows pets with supported locations or acquisition sources, plus pets with saved collection progress. Other entries are hidden until their sources are confirmed.";
        var footer = Ui.Text("Maps show spawn locations. Confirm shop stock in game.", 10, Ui.Muted);
        footer.Margin = new Thickness(0, 12, 0, 0);
        DockPanel.SetDock(footer, Dock.Bottom);
        layout.Children.Add(footer);
        var body = new Grid();
        body.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star), MinWidth = 260, MaxWidth = 320 });
        body.ColumnDefinitions.Add(new() { Width = new GridLength(1.55, GridUnitType.Star) });
        layout.Children.Add(body);
        ScrollViewer.SetHorizontalScrollBarVisibility(results, ScrollBarVisibility.Disabled);
        results.ItemTemplate = (DataTemplate)XamlReader.Parse("""
   <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:local="clr-namespace:PetTracker;assembly=PetTracker">
    <Grid ToolTip="{Binding Hint}">
     <Grid.ColumnDefinitions><ColumnDefinition Width="42"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
     <Image Source="{Binding Portrait}" Width="34" Height="40" Stretch="Uniform" Margin="0,0,8,0"/>
     <StackPanel Grid.Column="1">
     <TextBlock Text="{Binding Label}" FontSize="13" FontWeight="SemiBold" TextTrimming="CharacterEllipsis"/>
     <DockPanel Margin="0,6,0,0">
      <TextBlock DockPanel.Dock="Right" Text="{Binding Progress}" Foreground="{Binding ProgressBrush}" FontSize="10" FontWeight="SemiBold" VerticalAlignment="Center" Margin="6,0,0,0"/>
      <StackPanel Orientation="Horizontal"><TextBlock Text="{Binding Rank}" FontSize="10" Foreground="#99A3B1" VerticalAlignment="Center"/><local:PetFamilyBadge PetId="{Binding Result.Pet.Id}"/></StackPanel>
     </DockPanel>
     <TextBlock Text="{Binding Remaining}" FontSize="10" Foreground="#91A4B5" Margin="0,5,0,0">
      <TextBlock.Style><Style TargetType="TextBlock"><Style.Triggers><Trigger Property="Text" Value=""><Setter Property="Visibility" Value="Collapsed"/></Trigger></Style.Triggers></Style></TextBlock.Style>
     </TextBlock>
     </StackPanel>
    </Grid>
   </DataTemplate>
   """);
        results.SelectionChanged += (s, e) => RefreshDetail();
        System.Windows.Automation.AutomationProperties.SetAutomationId(results, "PetFinderResults");
        results.ItemContainerStyle = (Style)FindResource("FinderPetRow");
        var collection = new DockPanel();
        var listHeader = new DockPanel { Margin = new Thickness(10, 6, 10, 12) };
        DockPanel.SetDock(listHeader, Dock.Top);
        collection.Children.Add(listHeader);
        DockPanel.SetDock(count, Dock.Right);
        listHeader.Children.Add(count);
        var listTitle = Ui.Text("COLLECTION", 10, Ui.Muted);
        listTitle.FontWeight = FontWeights.SemiBold;
        listHeader.Children.Add(listTitle);
        collection.Children.Add(results);
        body.Children.Add(new Border { Child = collection, Background = new SolidColorBrush(Color.FromRgb(14, 20, 28)), BorderBrush = new SolidColorBrush(Color.FromRgb(30, 41, 54)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(6) });
        results.BorderThickness = new Thickness(0);
        results.Background = Brushes.Transparent;
        var scroll = new ScrollViewer
        {
            Content = detail,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        detail.Margin = new Thickness(0, 0, 8, 0);
        var card = new Border
        {
            Child = scroll,
            Background = new SolidColorBrush(Color.FromRgb(16, 23, 32)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(30, 41, 54)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(20),
            Margin = new Thickness(16, 0, 0, 0)
        };
        Grid.SetColumn(card, 1);
        body.Children.Add(card);
        search.TextChanged += (s, e) => Search();
        filter.SelectionChanged += (s, e) => Search();
        rankFilter.SelectionChanged += (s, e) => Search();
        regionFilter.SelectionChanged += (s, e) => Search();
        sort.SelectionChanged += (s, e) => Search();
        overview.Changed += Search;
        host.Changed += Refresh;
        Closed += (s, e) => host.Changed -= Refresh;
        Search();
        SelectPet(petId);
        SizeChanged += (_, _) =>
        {
            bool compact = ActualHeight < 600;
            overview.SetCompact(compact);
            overview.Margin = new Thickness(0, 0, -6, compact ? 10 : 14);
            collectionStatus.Margin = new Thickness(0, 5, 0, compact ? 10 : 14);
        };
        Loaded += (s, e) => search.Focus();
    }

    IEnumerable<(MarketStock Stock, MarketOffer Offer)> Offers() => host.View?.Ready == true ? (host.View.Market ?? Array.Empty<MarketStock>()).Where(s => s.Fresh(DateTimeOffset.UtcNow)).SelectMany(s => s.Offers.Select(o => (s, o))) : Array.Empty<(MarketStock, MarketOffer)>();
    IEnumerable<PetSearchResult> Matches()
    {
        var matches = filter.SelectedIndex == 0 ? PetFinder.Search(search.Text, host.Tracker.Profile) : BlackMarket.Watchlist(search.Text, host.Tracker.Profile);
        if (filter.SelectedIndex == 2)
            matches = matches.Where(p => Offers().Any(o => o.Offer.PetId == p.Pet.Id));
        return PetCollection.Select(matches, host.Tracker.Profile,
            new(overview.Family, (CollectionRankFilter)rankFilter.SelectedIndex, regionFilter.SelectedIndex == 0 ? "" : (string)regionFilter.SelectedItem, (CollectionSort)sort.SelectedIndex),
            host.View?.CollectionSynced == true);
    }

    void RefreshOverview()
    {
        var profile = host.Tracker.Profile;
        overview.Refresh(profile);
        var totals = PetCollection.Summarize(PetFinder.Search("", profile), profile);
        string state = host.View?.CollectionSynced == true ? "Collection synced" : "Saved collection · log in with the tracker running to sync";
        collectionStatus.Text = $"{state} · {totals.Maxed} maxed · {totals.Unknown} unknown rank";
        clearFilters.IsEnabled = search.Text.Length > 0 || filter.SelectedIndex != 0 || rankFilter.SelectedIndex != 0 || regionFilter.SelectedIndex != 0 || sort.SelectedIndex != 0 || overview.Family.Length > 0;
    }

    void ResetFilters()
    {
        changingFilters = true;
        try
        {
            search.Text = "";
            filter.SelectedIndex = rankFilter.SelectedIndex = regionFilter.SelectedIndex = sort.SelectedIndex = 0;
            overview.Select("");
        }
        finally { changingFilters = false; }
        Search();
    }

    void MarketHelp()
    {
        marketHelp.Text = filter.SelectedIndex == 0 ? "" : filter.SelectedIndex == 1 ? "Shopping list · Chest, merchant and dungeon pets. Maxed pets are hidden." : "Recent offers · Open Special and Pets in game to read stock. Offers clear after two minutes or a connection change.";
        if (filter.SelectedIndex != 0 && host.View?.CollectionSynced != true)
            marketHelp.Text += " " + TrackingGuidance.Sync;
        marketHelp.Margin = new Thickness(0, 0, 0, filter.SelectedIndex == 0 ? 0 : 12);
    }

    void Search()
    {
        if (changingFilters)
            return;
        RefreshOverview();
        int? selected = (results.SelectedItem as SearchRow)?.Result.Pet.Id;
        var matches = Matches().Select(p => new SearchRow(p, portraits.Get(p.Pet.Id))).ToArray();
        foreach (var row in matches)
            row.Update(host.Tracker.Profile, host.View?.CollectionSynced == true);
        detailRendered = false;
        results.ItemsSource = matches;
        count.Text = matches.Length == 1 ? "1 pet" : $"{matches.Length} pets";
        MarketHelp();
        results.SelectedItem = matches.FirstOrDefault(p => p.Result.Pet.Id == selected) ?? matches.FirstOrDefault();
        RefreshDetail();
    }

    public void SelectPet(int? id)
    {
        if (id == null)
            return;
        ResetFilters();
        results.SelectedItem = results.Items.Cast<SearchRow>().FirstOrDefault(p => p.Result.Pet.Id == id);
        if (results.SelectedItem != null)
            results.ScrollIntoView(results.SelectedItem);
    }

    void Refresh()
    {
        RefreshOverview();
        MarketHelp();
        if (!results.Items.Cast<SearchRow>().Select(r => r.Result.Pet.Id).SequenceEqual(Matches().Select(r => r.Pet.Id)))
        {
            Search();
            return;
        }

        foreach (var row in results.Items.Cast<SearchRow>())
            row.Update(host.Tracker.Profile, host.View?.CollectionSynced == true);
        RefreshDetail();
    }

    void RefreshDetail()
    {
        int? selectedId = (results.SelectedItem as SearchRow)?.Result.Pet.Id;
        string pinKey = host.PetOverlayPreferences.PinLimit + ":" + string.Join(",", host.PetOverlayPreferences.Pins());
        var today = DateOnly.FromDateTime(DateTime.Now);
        string marketKey = string.Join(";", Offers().Where(o => o.Offer.PetId == selectedId).Select(o => $"{o.Stock.Tab}:{o.Stock.SeenUtc:O}:{o.Offer.Price}"));
        if (detailRendered && renderedPet == selectedId && renderedPins == pinKey && renderedDate == today && renderedMarket == marketKey)
            return;
        detailRendered = true;
        renderedPet = selectedId;
        renderedPins = pinKey;
        renderedDate = today;
        renderedMarket = marketKey;
        detail.Children.Clear();
        if (results.SelectedItem is not SearchRow selectedRow)
        {
            detail.Children.Add(Ui.Text(filter.SelectedIndex == 2 ? "No recent matching shop offers. Open the Black Market in game while Pet Tracker is connected." : "No matching pets. Try another search or clear the filters.", 13, Ui.Muted));
            return;
        }

        var selected = selectedRow.Result;
        int id = selected.Pet.Id;
        var heading = new DockPanel();
        var portrait = new Image { Source = portraits.Get(id), Width = 60, Height = 64, Stretch = Stretch.Uniform };
        var portraitWell = new Border
        {
            Child = portrait,
            Background = new SolidColorBrush(Color.FromRgb(23, 32, 43)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(6),
            Margin = new Thickness(0, 0, 14, 0)
        };
        DockPanel.SetDock(portraitWell, Dock.Left);
        heading.Children.Add(portraitWell);
        var identity = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        identity.Children.Add(new TextBlock { Text = selected.Pet.Name, FontSize = 21, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        identity.Children.Add(new PetFamilyBadge(id) { Margin = new Thickness(0, 8, 0, 0) });
        heading.Children.Add(identity);
        detail.Children.Add(heading);
        var progressPanel = new StackPanel
        {
            Margin = new Thickness(0, 0, 16, 0),
            VerticalAlignment = VerticalAlignment.Center,
            DataContext = selectedRow
        };
        progressPanel.SetBinding(ToolTipProperty, new System.Windows.Data.Binding("Hint"));
        var progress = new DockPanel();
        var rank = Ui.Text("", 12, Ui.Muted);
        rank.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Rank"));
        var amount = Ui.Text("", 12);
        amount.FontWeight = FontWeights.SemiBold;
        amount.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Progress"));
        amount.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("ProgressBrush"));
        DockPanel.SetDock(amount, Dock.Right);
        progress.Children.Add(amount);
        progress.Children.Add(rank);
        progressPanel.Children.Add(progress);
        var bar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Style = (Style)FindResource("FinderProgress"),
            Margin = new Thickness(0, 9, 0, 0)
        };
        bar.SetBinding(ProgressBar.ValueProperty, new System.Windows.Data.Binding("ProgressValue") { Mode = System.Windows.Data.BindingMode.OneWay });
        bar.SetBinding(ProgressBar.VisibilityProperty, new System.Windows.Data.Binding("ProgressVisibility"));
        System.Windows.Automation.AutomationProperties.SetAutomationId(bar, "FinderSoulProgress");
        progressPanel.Children.Add(bar);
        var prefs = host.PetOverlayPreferences;
        bool pinned = prefs.IsPinned(id);
        var pin = Ui.Button(pinned ? "Unpin pet" : prefs.CanPin(id) ? "Pin this pet" : "Pin limit reached", async () => await host.PinPet(id, !pinned));
        pin.Style = (Style)FindResource("FinderPin");
        pin.Margin = new Thickness(0);
        pin.Padding = new Thickness(16, 7, 16, 7);
        pin.HorizontalAlignment = HorizontalAlignment.Left;
        pin.IsEnabled = prefs.CanPin(id);
        pin.ToolTip = PetPins.Hint(prefs, id);
        ToolTipService.SetShowOnDisabled(pin, true);
        var summary = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        summary.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        summary.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        summary.Children.Add(progressPanel);
        Grid.SetColumn(pin, 1);
        summary.Children.Add(pin);
        detail.Children.Add(summary);
        var observed = Offers().Where(o => o.Offer.PetId == id).ToArray();
        if (observed.Length > 0)
        {
            Section("Seen in Black Market");
            foreach (var offer in observed)
                Paragraph($"{offer.Stock.TabName} · {offer.Offer.Price:N0} Kina · seen {offer.Stock.SeenUtc.ToLocalTime():t}");
            Paragraph("Check the offer in game before buying. Owned pet items convert to souls; the tracker does not buy or refresh stock.");
        }

        void AcquisitionDetails()
        {
            if (selected.Acquisition is { } acquisition)
            {
                Section(acquisition.Kind);
                Paragraph(acquisition.Instructions);
                Paragraph(acquisition.Window);
                Paragraph(acquisition.Availability(today));
                var sourceButton = Ui.Button(acquisition.SourceLabel, () => Open(acquisition.SourceUrl));
                sourceButton.HorizontalAlignment = HorizontalAlignment.Left;
                detail.Children.Add(sourceButton);
                sourceButton.ToolTip = acquisition.SourceName + " · checked " + acquisition.CheckedOn;
            }
        }

        if (selected.AvailableMaps.Count == 0)
            AcquisitionDetails();
        if (selected.Pet.Category == "Special" && selected.Location?.Maps.Count is not > 0 && selected.Creatures.Count == 0)
        {
            if (selected.Acquisition == null)
            {
                Section("How to obtain");
                Paragraph("No confirmed acquisition source yet.");
            }

            return;
        }

        if (selected.AvailableMaps.Count == 0 && selected.Creatures.Count == 0 && selected.Acquisition != null)
            return;
        Section(selected.AvailableMaps.Count > 0 ? "Locations" : "Soul sources");
        var shownRegions = new HashSet<string>();
        if (selected.AvailableMaps.Count > 0)
        {
            foreach (var map in selected.AvailableMaps.OrderBy(m => m.Region == (string?)regionFilter.SelectedItem ? 0 : 1))
            {
                var location = new StackPanel();
                var button = Ui.Button(map.Region + " ↗", () => Open(map.Url));
                button.ToolTip = "Open farming map · " + map.Region;
                button.HorizontalAlignment = HorizontalAlignment.Left;
                button.Margin = new Thickness(0, 0, 0, 8);
                location.Children.Add(button);
                var mobs = selected.Location?.SoulSources?.Where(s => s.Region == map.Region).Select(s => s.Name).Distinct().OrderBy(n => n).ToArray();
                if (mobs is { Length: > 0 })
                {
                    shownRegions.Add(map.Region);
                    var names = Ui.Text(string.Join("\n", mobs), 12, Ui.Muted);
                    names.LineHeight = 19;
                    location.Children.Add(names);
                }

                detail.Children.Add(new Border { Child = location, Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 8), Background = new SolidColorBrush(Color.FromRgb(20, 29, 40)), CornerRadius = new CornerRadius(6) });
            }
        }
        else if (selected.Creatures.Count == 0)
            Paragraph("No confirmed farming location yet.");
        if (selected.Location?.SoulSources is { Count: > 0 } sources)
        {
            foreach (var group in sources.Where(s => !shownRegions.Contains(s.Region)).GroupBy(s => s.Region).OrderBy(g => g.Key))
            {
                Section(group.Key);
                Paragraph(string.Join("\n", group.Select(s => s.Name).Distinct().OrderBy(n => n)));
            }
        }
        else if (selected.Creatures.Count > 0)
        {
            Section("Soul drops");
            Paragraph(string.Join("\n", selected.Creatures));
        }

        if (selected.AvailableMaps.Count > 0)
            AcquisitionDetails();
        if (selected.Location?.SourceUrl is { } source && selected.AvailableMaps.Count > 0 && selected.Acquisition?.SourceUrl != source)
        {
            var button = Ui.Button("Source details ↗", () => Open(source));
            button.ToolTip = "Aion 2 Maps · source may include regions outside the current game release";
            button.Background = Brushes.Transparent;
            button.HorizontalAlignment = HorizontalAlignment.Left;
            button.Margin = new Thickness(0, 8, 0, 0);
            detail.Children.Add(button);
        }
    }

    void Section(string title)
    {
        var text = Ui.Text(title, 12);
        text.FontWeight = FontWeights.SemiBold;
        detail.Children.Add(new Border { Child = text, BorderBrush = new SolidColorBrush(Color.FromRgb(35, 46, 59)), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 14, 0, 0), Margin = new Thickness(0, 18, 0, 10) });
    }

    void Paragraph(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        var text = Ui.Text(value, 12, Ui.Muted);
        text.LineHeight = 19;
        text.Margin = new Thickness(0, 0, 0, 10);
        detail.Children.Add(text);
    }

    void Open(string url)
    {
        try
        {
            if (!PetFinder.AllowedSourceUrl(url))
                throw new InvalidDataException("This source link is unavailable.");
            if (!host.PreviewOnly)
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is InvalidDataException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Ui.Error(ex, this);
        }
    }
}
