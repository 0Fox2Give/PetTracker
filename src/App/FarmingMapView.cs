using System.Windows.Automation;

namespace PetTracker;

// The finder owns selection and filters; this pane only chooses among that pet's maps.
public sealed class FarmingMapView : Grid
{
    readonly ComboBox region = new() { Width = 135, Height = 30, DisplayMemberPath = "Name", ToolTip = "Map region" };
    readonly TextBlock title = Ui.Text("", 14);
    readonly TextBlock regionName = Ui.Text("", 11, Ui.Muted);
    readonly TextBlock hint = Ui.Text("", 10, Ui.Muted);
    readonly TextBlock emptyText = Ui.Text("", 13, Ui.Muted);
    readonly StackPanel empty = new() { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 290, Margin = new Thickness(16) };
    readonly Button external;
    readonly WrapPanel actions = new();
    readonly FarmingMapCanvas map = new(Path.Combine(AppContext.BaseDirectory, "maps")) { Focusable = true };
    PetSearchResult? pet;
    string context = "";
    string? externalUrl;
    bool changingRegion;
    bool needsFit;
    int pointCount;

    public FarmingMapView(Action<string> openSource)
    {
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowDefinitions.Add(new());
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        title.FontWeight = FontWeights.SemiBold;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.Margin = new Thickness(0, 0, 0, 8);
        header.Children.Add(title);
        header.Children.Add(actions);
        Children.Add(header);
        region.Margin = new Thickness(0, 0, 6, 4);
        regionName.Margin = new Thickness(0, 0, 10, 4);
        regionName.VerticalAlignment = VerticalAlignment.Center;
        regionName.Height = 30;
        regionName.Padding = new Thickness(0, 7, 0, 0);
        actions.Children.Add(regionName);
        actions.Children.Add(region);
        foreach (var (label, action) in new (string, Action)[] { ("−", () => map.ZoomBy(.8)), ("+", () => map.ZoomBy(1.25)), ("Fit spawns", map.FitPoints), ("Reset", map.Reset) })
        {
            var button = Ui.Button(label, action);
            button.Height = 30;
            button.Padding = new Thickness(10, 2, 10, 2);
            button.Margin = new Thickness(0, 0, 6, 4);
            button.ToolTip = label == "−" ? "Zoom out" : label == "+" ? "Zoom in" : label == "Reset" ? "Show the whole region" : "Frame this pet's spawn locations";
            actions.Children.Add(button);
        }
        var surface = new Grid { ClipToBounds = true };
        surface.Children.Add(map);
        emptyText.TextAlignment = TextAlignment.Center;
        emptyText.TextWrapping = TextWrapping.Wrap;
        empty.Children.Add(emptyText);
        external = Ui.Button("Open source map ↗", () => { if (externalUrl != null) openSource(externalUrl); });
        external.HorizontalAlignment = HorizontalAlignment.Center;
        external.Margin = new Thickness(0, 12, 0, 0);
        empty.Children.Add(external);
        surface.Children.Add(empty);
        var border = new Border { Child = surface, BorderBrush = new SolidColorBrush(Color.FromRgb(46, 60, 73)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), ClipToBounds = true };
        Grid.SetRow(border, 1);
        Children.Add(border);
        hint.Margin = new Thickness(0, 8, 0, 0);
        hint.TextWrapping = TextWrapping.Wrap;
        hint.ToolTip = "Drag or use arrow keys to pan. Scroll to zoom. Hover a dot for the mob name.\nMap art © NCSOFT · Data: SkeeveAN/Aion-DPS-Meter";
        Grid.SetRow(hint, 2);
        Children.Add(hint);
        AutomationProperties.SetAutomationId(region, "MapRegion");
        AutomationProperties.SetName(region, "Map region for selected pet");
        AutomationProperties.SetAutomationId(map, "FarmingMap");
        AutomationProperties.SetAutomationId(emptyText, "MapEmptyState");
        region.SelectionChanged += (_, _) => { if (!changingRegion) RenderRegion(); };
        Loaded += (_, _) =>
        {
            if (needsFit) map.FitPoints();
            needsFit = false;
        };
        SizeChanged += (_, _) => RefreshLayout();
    }

    public void ShowPet(PetSearchResult? selected, string preferredRegion)
    {
        pet = selected;
        title.Text = selected?.Label ?? "No pet selected";
        title.ToolTip = title.Text;
        string key = selected?.Pet.Id + ":" + preferredRegion;
        if (context == key)
            return;
        context = key;
        var choices = FarmingAtlas.Regions.Where(r => (preferredRegion.Length == 0 || r.Name == preferredRegion) && r.Points.Any(p => p.PetId == selected?.Pet.Id)).ToArray();
        var previous = region.SelectedItem as SpawnRegion;
        changingRegion = true;
        region.ItemsSource = choices;
        region.SelectedItem = choices.Contains(previous) ? previous : choices.FirstOrDefault();
        region.Visibility = choices.Length > 1 ? Visibility.Visible : Visibility.Collapsed;
        regionName.Visibility = choices.Length > 1 ? Visibility.Collapsed : Visibility.Visible;
        changingRegion = false;
        externalUrl = selected?.AvailableMaps.FirstOrDefault(m => preferredRegion.Length == 0 || m.Region == preferredRegion)?.Url;
        emptyText.Text = selected == null ? "Select a pet from the collection to see its spawn locations." : preferredRegion.Length > 0 ? $"No built-in map for {selected.Label} in {preferredRegion}." : $"No mapped spawn locations for {selected.Label}.";
        RenderRegion();
    }

    void RenderRegion()
    {
        bool available = region.SelectedItem is SpawnRegion;
        actions.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        map.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        empty.Visibility = available ? Visibility.Collapsed : Visibility.Visible;
        external.Visibility = externalUrl != null ? Visibility.Visible : Visibility.Collapsed;
        if (region.SelectedItem is SpawnRegion area)
        {
            regionName.Text = area.Name;
            map.SetRegion(area);
            var points = area.Points.Where(p => p.PetId == pet?.Pet.Id).ToArray();
            map.SetPoints(points);
            // A region change can keep the pet ID, but still needs a new viewport.
            map.FitPoints();
            needsFit = !IsLoaded || map.ActualWidth < 1;
            pointCount = points.Length;
        }
        else
        {
            map.SetPoints(Array.Empty<SpawnPoint>());
            pointCount = 0;
        }
        RefreshLayout();
    }

    void RefreshLayout()
    {
        bool compact = ActualHeight < 240;
        title.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        hint.Text = region.SelectedItem == null ? "See Details for other sources." : compact
            ? $"{pointCount} spawns · Approximate locations"
            : $"{pointCount} spawn points · Approximate locations\nDrag to pan · Scroll to zoom · Hover a dot for the mob name";
    }
}
