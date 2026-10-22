using System.Globalization;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace PetTracker;

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
