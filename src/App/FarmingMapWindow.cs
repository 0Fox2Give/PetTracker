using System.Globalization;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace PetTracker;

public sealed class FarmingMapWindow : Window
{
    readonly Host host;
    readonly ComboBox region = new() { ItemsSource = FarmingAtlas.Regions, DisplayMemberPath = "Name", Width = 160, SelectedIndex = 0 };
    readonly ComboBox family = new() { ItemsSource = new[] { "All families", "Cogni", "Fera", "Natura", "Varian", "Special" }, Width = 140, SelectedIndex = 0 };
    readonly TextBox search = new() { Height = 32, Padding = new Thickness(8, 2, 8, 2), VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "Search pets or mobs" };
    readonly CheckBox hideMaxed = new() { Content = "Hide maxed", VerticalAlignment = VerticalAlignment.Center };
    readonly ListBox pets = new() { DisplayMemberPath = "Label", BorderThickness = new Thickness(0) };
    readonly TextBlock count = Ui.Text("", 11, Ui.Muted);
    readonly TextBlock selected = Ui.Text("", 14);
    readonly TextBlock hint = Ui.Text("", 11, Ui.Muted);
    readonly FarmingMapCanvas map;
    bool refreshing;

    public FarmingMapWindow(Host host, int? petId = null)
    {
        this.host = host;
        map = new(Path.Combine(AppContext.BaseDirectory, "maps")) { Focusable = true };
        Title = "Pet Tracker — Farming map";
        Width = 1040;
        Height = 720;
        MinWidth = 760;
        MinHeight = 520;
        NativeTheme.Install(this);
        NativeTheme.DarkTitleBar(this);
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/PetTracker;component/FinderStyles.xaml", UriKind.Relative) });
        pets.ItemContainerStyle = (Style)FindResource("FinderPetRow");
        search.Tag = "Search pets or mobs…";
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var layout = new DockPanel { Margin = new Thickness(22) };
        Content = layout;
        var heading = new StackPanel();
        DockPanel.SetDock(heading, Dock.Top);
        layout.Children.Add(heading);
        heading.Children.Add(new TextBlock { Text = "Farming map", FontSize = 24, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock { Text = "Pick a pet to see its known spawn locations.", Foreground = Ui.Muted, Margin = new Thickness(0, 5, 0, 16) });
        var tools = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        foreach (var control in new FrameworkElement[] { region, family, hideMaxed })
        { control.Margin = new Thickness(0, 0, 12, 4); tools.Children.Add(control); }
        heading.Children.Add(tools);
        var footer = new TextBlock { Text = "Approximate spawn locations · Static map, not live sightings\nMap art © NCSOFT · Data: SkeeveAN/Aion-DPS-Meter", FontSize = 10, Foreground = Ui.Muted, Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom);
        layout.Children.Add(footer);
        var columns = new Grid();
        columns.ColumnDefinitions.Add(new() { Width = new GridLength(230) });
        columns.ColumnDefinitions.Add(new());
        layout.Children.Add(columns);
        var sidebar = new DockPanel { Margin = new Thickness(0, 0, 16, 0) };
        var filterPanel = new StackPanel();
        filterPanel.Children.Add(Ui.Text("Search pets or mobs", 11, Ui.Muted));
        filterPanel.Children.Add(search);
        count.Margin = new Thickness(0, 8, 0, 10);
        filterPanel.Children.Add(count);
        DockPanel.SetDock(filterPanel, Dock.Top);
        sidebar.Children.Add(filterPanel);
        sidebar.Children.Add(pets);
        columns.Children.Add(sidebar);
        var pane = new DockPanel();
        Grid.SetColumn(pane, 1);
        columns.Children.Add(pane);
        var mapHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(Ui.Button("−", () => map.ZoomBy(.8)));
        actions.Children.Add(Ui.Button("+", () => map.ZoomBy(1.25)));
        actions.Children.Add(Ui.Button("Fit spawns", map.FitPoints));
        actions.Children.Add(Ui.Button("Reset", map.Reset));
        DockPanel.SetDock(actions, Dock.Right);
        mapHeader.Children.Add(actions);
        mapHeader.Children.Add(selected);
        DockPanel.SetDock(mapHeader, Dock.Top);
        pane.Children.Add(mapHeader);
        hint.Margin = new Thickness(0, 8, 0, 0);
        hint.TextWrapping = TextWrapping.Wrap;
        DockPanel.SetDock(hint, Dock.Bottom);
        pane.Children.Add(hint);
        pane.Children.Add(new Border { Child = map, BorderBrush = new SolidColorBrush(Color.FromRgb(46, 60, 73)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), ClipToBounds = true });
        Id(region, "MapRegion");
        Id(family, "MapFamily");
        Id(hideMaxed, "MapHideMaxed");
        Id(search, "MapSearch");
        Id(pets, "MapPets");
        Id(map, "FarmingMap");
        region.SelectionChanged += (_, _) => { map.SetRegion((SpawnRegion)region.SelectedItem); Refresh(); };
        family.SelectionChanged += (_, _) => Refresh();
        search.TextChanged += (_, _) => Refresh();
        hideMaxed.Checked += (_, _) => Refresh();
        hideMaxed.Unchecked += (_, _) => Refresh();
        pets.SelectionChanged += (_, _) => { if (!refreshing) Select(); };
        host.Changed += Refresh;
        Closed += (_, _) => host.Changed -= Refresh;
        map.SetRegion((SpawnRegion)region.SelectedItem);
        Refresh();
        SelectPet(petId);
        Loaded += (_, _) => map.FitPoints();
    }

    static void Id(DependencyObject control, string value) => System.Windows.Automation.AutomationProperties.SetAutomationId(control, value);
    public void SelectPet(int? id)
    {
        if (id == null)
            return;
        var match = FarmingAtlas.Regions.FirstOrDefault(r => r.Points.Any(p => p.PetId == id));
        if (match == null)
            return;
        search.Clear();
        family.SelectedIndex = 0;
        hideMaxed.IsChecked = false;
        region.SelectedItem = match;
        Refresh();
        pets.SelectedItem = pets.Items.Cast<PetSearchResult>().FirstOrDefault(p => p.Pet.Id == id);
        if (pets.SelectedItem != null)
            pets.ScrollIntoView(pets.SelectedItem);
    }

    void Refresh()
    {
        int? id = (pets.SelectedItem as PetSearchResult)?.Pet.Id;
        var matches = FarmingAtlas.Pets((SpawnRegion)region.SelectedItem, host.Tracker.Profile, search.Text, family.SelectedIndex == 0 ? "" : (string)family.SelectedItem, hideMaxed.IsChecked == true).ToArray();
        if (!pets.Items.Cast<PetSearchResult>().Select(p => p.Pet.Id).SequenceEqual(matches.Select(p => p.Pet.Id)))
        {
            refreshing = true;
            pets.ItemsSource = matches;
            pets.SelectedItem = matches.FirstOrDefault(p => p.Pet.Id == id) ?? matches.FirstOrDefault();
            refreshing = false;
        }
        count.Text = $"{matches.Length} pets with mapped spawns";
        Select();
    }

    void Select()
    {
        var pet = pets.SelectedItem as PetSearchResult;
        var points = pet == null ? Array.Empty<SpawnPoint>() : ((SpawnRegion)region.SelectedItem).Points.Where(p => p.PetId == pet.Pet.Id).ToArray();
        selected.Text = pet?.Label ?? "No matching pets";
        selected.ToolTip = pet?.Label;
        selected.TextTrimming = TextTrimming.CharacterEllipsis;
        selected.VerticalAlignment = VerticalAlignment.Center;
        map.SetPoints(points);
        hint.Text = points.Length == 0 ? "Try another search or family. Only pets with audited regional sources are listed." : $"{points.Length} spawn points · Drag or arrow keys to pan · Scroll to zoom · Hover a dot for the mob name";
    }
}

public sealed class FarmingMapCanvas(string assetRoot) : FrameworkElement
{
    readonly Dictionary<MapTile, BitmapSource> images = new();
    SpawnRegion? region;
    IReadOnlyList<SpawnPoint> points = Array.Empty<SpawnPoint>();
    double zoom = 1;
    Point center = new(.5, .5);
    Point? drag;
    bool missingTiles;
    public double Zoom => zoom;
    public Point Center => center;
    public int PointCount => points.Count;
    public bool HasCompleteMap => region != null && !missingTiles && images.Count == region.Tiles.Count;
    double Span => Math.Max(1, Math.Min(ActualWidth, ActualHeight)) * zoom;

    public void SetRegion(SpawnRegion value)
    {
        if (region == value)
            return;
        region = value;
        images.Clear();
        missingTiles = false;
        foreach (var tile in value.Tiles)
        {
            string path = Path.Combine(assetRoot, value.Key, $"{tile.Column}_{tile.Row}.jpg");
            if (!File.Exists(path))
            { missingTiles = true; continue; }
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 512;
                bitmap.UriSource = new Uri(path);
                bitmap.EndInit();
                bitmap.Freeze();
                images[tile] = bitmap;
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException)
            {
                missingTiles = true;
            }
        }
        Reset();
    }
    public void SetPoints(IReadOnlyList<SpawnPoint> value)
    {
        bool changed = points.FirstOrDefault()?.PetId != value.FirstOrDefault()?.PetId;
        points = value;
        if (changed && ActualWidth > 0)
            FitPoints();
        InvalidateVisual();
    }
    public void Reset()
    {
        if (region?.Points.Count > 0 && ActualWidth > 0)
            Fit(region.Points);
        else
        { zoom = 1; center = new(.5, .5); }
        InvalidateVisual();
    }
    public void FitPoints()
    {
        if (points.Count == 0)
        { Reset(); return; }
        Fit(points);
    }
    void Fit(IReadOnlyList<SpawnPoint> locations)
    {
        double left = locations.Min(p => p.X), right = locations.Max(p => p.X), top = locations.Min(p => p.Y), bottom = locations.Max(p => p.Y);
        center = new((left + right) / 2, (top + bottom) / 2);
        double fit = Math.Min(ActualWidth / Math.Max(.08, right - left), ActualHeight / Math.Max(.08, bottom - top));
        zoom = Math.Clamp(.8 * fit / Math.Max(1, Math.Min(ActualWidth, ActualHeight)), 1, 8);
        InvalidateVisual();
    }
    public void ZoomBy(double factor) => ZoomAt(factor, new(ActualWidth / 2, ActualHeight / 2));
    void ZoomAt(double factor, Point anchor)
    {
        double old = Span;
        zoom = Math.Clamp(zoom * factor, 1, 8);
        center = new(Math.Clamp(center.X + (anchor.X - ActualWidth / 2) * (1 / old - 1 / Span), 0, 1), Math.Clamp(center.Y + (anchor.Y - ActualHeight / 2) * (1 / old - 1 / Span), 0, 1));
        InvalidateVisual();
    }
    Point Screen(double x, double y) => new((x - center.X) * Span + ActualWidth / 2, (y - center.Y) * Span + ActualHeight / 2);
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.PushClip(new RectangleGeometry(new Rect(RenderSize)));
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(30, 40, 44)), null, new Rect(RenderSize));
        if (region != null)
        {
            foreach (var tile in images)
            {
                var start = Screen((double)tile.Key.Column / region.Grid, (double)tile.Key.Row / region.Grid);
                var rect = new Rect(start, new Size(Span / region.Grid + .3, Span / region.Grid + .3));
                if (rect.IntersectsWith(new Rect(RenderSize)))
                    dc.DrawImage(tile.Value, rect);
            }
            // Do not paint meaningful-looking dots over a missing map.
            if (!missingTiles)
                foreach (var point in points)
                {
                    var p = Screen(point.X, point.Y);
                    if (p.X < -5 || p.Y < -5 || p.X > ActualWidth + 5 || p.Y > ActualHeight + 5)
                        continue;
                    dc.DrawEllipse(Brushes.Gold, new Pen(Brushes.Black, 1.5), p, 3.5, 3.5);
                }
        }
        if (missingTiles)
            dc.DrawText(new FormattedText("Map files are missing. Reinstall Pet Tracker to restore them.", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip), new Point(18, 18));
        dc.Pop();
    }
    protected override void OnMouseWheel(MouseWheelEventArgs e) { ZoomAt(e.Delta > 0 ? 1.2 : 1 / 1.2, e.GetPosition(this)); e.Handled = true; }
    protected override void OnMouseDown(MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left) { Focus(); drag = e.GetPosition(this); CaptureMouse(); Cursor = Cursors.Hand; } }
    protected override void OnMouseUp(MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left) { drag = null; ReleaseMouseCapture(); Cursor = Cursors.Arrow; } }
    protected override void OnLostMouseCapture(MouseEventArgs e) { drag = null; Cursor = Cursors.Arrow; }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        double step = .1 / zoom;
        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            center = new(Math.Clamp(center.X + (e.Key == Key.Right ? step : e.Key == Key.Left ? -step : 0), 0, 1), Math.Clamp(center.Y + (e.Key == Key.Down ? step : e.Key == Key.Up ? -step : 0), 0, 1));
            e.Handled = true;
            InvalidateVisual();
        }
        base.OnKeyDown(e);
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        var at = e.GetPosition(this);
        if (drag is Point from)
        {
            center = new(Math.Clamp(center.X - (at.X - from.X) / Span, 0, 1), Math.Clamp(center.Y - (at.Y - from.Y) / Span, 0, 1));
            drag = at;
            InvalidateVisual();
            return;
        }
        var nearest = points.Select(p => (Point: p, Distance: (Screen(p.X, p.Y) - at).LengthSquared)).OrderBy(p => p.Distance).FirstOrDefault();
        ToolTip = !missingTiles && nearest.Point != null && nearest.Distance <= 100 ? nearest.Point.Name : null;
    }
}
