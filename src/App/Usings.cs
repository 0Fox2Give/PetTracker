global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.IO;
global using System.Threading;
global using System.Threading.Tasks;
global using System.Windows;
global using System.Windows.Controls;
global using System.Windows.Media;

namespace PetTracker;

public static class Ui
{
    public static Brush Ink = new SolidColorBrush(Color.FromRgb(223, 230, 239)), Muted = new SolidColorBrush(Color.FromRgb(145, 155, 171)), Dark = new SolidColorBrush(Color.FromRgb(11, 16, 23));
    public static TextBlock Text(string value, double size = 13, Brush? brush = null) => new() { Text = value, FontSize = size, Foreground = brush ?? Ink, TextWrapping = TextWrapping.Wrap };
    public static Button Button(string text, Action click)
    {
        var b = new Button { Content = text, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0) };
        b.Click += (s, e) => click();
        return b;
    }

    public static void Error(Exception ex, Window? owner = null)
    {
        if (owner != null)
            MessageBox.Show(owner, ex.Message, "Pet Tracker", MessageBoxButton.OK, MessageBoxImage.Information);
        else
            MessageBox.Show(ex.Message, "Pet Tracker", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
