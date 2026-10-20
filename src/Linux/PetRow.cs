using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace PetTracker.Linux;

public sealed class PetRow : Border
{
    static readonly Dictionary<string, Bitmap> images = new();
    public PetRow(PetProgress pet, long? price = null, string? notice = null)
    {
        Padding = new Thickness(0, 10);
        BorderThickness = new Thickness(0, 0, 0, 1);
        BorderBrush = Brush.Parse("#303945");
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("44,*"), ColumnSpacing = 10 };
        var portrait = Picture($"portraits/{pet.Id}.png", 42);
        grid.Children.Add(portrait);
        var content = new StackPanel { Spacing = 5 };
        Grid.SetColumn(content, 1);
        grid.Children.Add(content);
        content.Children.Add(new TextBlock { Text = new PetNames().Resolve(pet), FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        var details = new DockPanel();
        var progress = new TextBlock { Text = pet.ProgressText, FontWeight = FontWeight.SemiBold };
        DockPanel.SetDock(progress, Dock.Right);
        details.Children.Add(progress);
        var rank = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        rank.Children.Add(new TextBlock { Text = pet.Level is int level ? $"Rank {level}" : "Rank ?", Foreground = Brushes.LightSlateGray });
        string family = PetFamily.Label(pet.Id);
        if (family.Length > 0)
        {
            rank.Children.Add(Picture($"crystals/{family.ToLowerInvariant()}.png", 16));
            rank.Children.Add(new TextBlock { Text = family, FontSize = 11 });
        }

        details.Children.Add(rank);
        content.Children.Add(details);
        content.Children.Add(new ProgressBar { Minimum = 0, Maximum = 1, Value = Activity.Ratio(pet), Height = 3, Foreground = Brushes.Goldenrod });
        if (price != null)
            content.Children.Add(new TextBlock { Text = $"{price:N0} Kina", Foreground = Brushes.Goldenrod });
        if (!string.IsNullOrEmpty(notice))
            content.Children.Add(new TextBlock { Text = notice, Foreground = Brushes.LightSlateGray, FontSize = 11 });
        if (pet.RankIncreasePending)
            content.Children.Add(new TextBlock { Text = "Rank increase pending", Foreground = Brushes.Goldenrod, FontSize = 11 });
        Child = grid;
    }

    static Image Picture(string relative, double size)
    {
        string path = Path.Combine(AppContext.BaseDirectory, relative);
        if (!images.TryGetValue(path, out var bitmap) && File.Exists(path))
            images[path] = bitmap = new Bitmap(path);
        return new Image { Source = bitmap, Width = size, Height = size, Stretch = Stretch.Uniform };
    }
}
