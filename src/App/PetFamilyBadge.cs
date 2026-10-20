using System.Windows.Media.Imaging;

namespace PetTracker;

public sealed class PetFamilyBadge : Border
{
    public static readonly DependencyProperty PetIdProperty = DependencyProperty.Register(nameof(PetId), typeof(int), typeof(PetFamilyBadge), new PropertyMetadata(0, (d, e) => ((PetFamilyBadge)d).Refresh()));
    public int PetId
    {
        get => (int)GetValue(PetIdProperty); set => SetValue(PetIdProperty, value);
    }
    static readonly Dictionary<int, string> categories = Catalog.Entries.ToDictionary(p => p.Id, p => p.Category);
    static readonly Dictionary<string, BitmapImage> icons = new();
    public static string Label(int id) => categories.GetValueOrDefault(id) switch { "Intellect" => "Cogni", "Feral" => "Fera", "Nature" => "Natura", "Trans" => "Varian", "Special" => "Special", _ => "" };
    public PetFamilyBadge()
    {
        CornerRadius = new CornerRadius(3);
        Padding = new Thickness(2, 0, 4, 0);
        Margin = new Thickness(6, 0, 0, 0);
        VerticalAlignment = VerticalAlignment.Center;
        HorizontalAlignment = HorizontalAlignment.Left;
        Background = new SolidColorBrush(Color.FromRgb(28, 35, 44));
        BorderBrush = new SolidColorBrush(Color.FromRgb(65, 75, 88));
        BorderThickness = new Thickness(1);
        Refresh();
    }
    public PetFamilyBadge(int id) : this()
    {
        PetId = id;
    }
    void Refresh()
    {
        string label = Label(PetId);
        Visibility = label.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (label.Length == 0)
        {
            Child = null;
            return;
        }
        if (!icons.TryGetValue(label, out var icon))
        {
            icon = new BitmapImage(new Uri($"pack://application:,,,/PetTracker;component/Crystals/{label.ToLowerInvariant()}.png"));
            icon.Freeze();
            icons.Add(label, icon);
        }
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new Image { Source = icon, Width = 18, Height = 18, Stretch = Stretch.Uniform });
        content.Children.Add(new TextBlock { Text = label, FontSize = 10, Foreground = Ui.Ink, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) });
        Child = content;
        ToolTip = label + " pet family";
        System.Windows.Automation.AutomationProperties.SetName(this, label + " pet family");
    }
}
