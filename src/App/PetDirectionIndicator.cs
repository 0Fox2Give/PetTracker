using System.Globalization;
using System.Windows.Automation;
using System.Windows.Shapes;

namespace PetTracker;
public sealed class PetDirectionIndicator : StackPanel
{
    readonly System.Windows.Shapes.Path arrow;
    readonly TextBlock distance;
    public PetDirectionIndicator()
    {
        Width = 42;
        Margin = new Thickness(8, 0, 0, 0);
        VerticalAlignment = VerticalAlignment.Center;
        Visibility = Visibility.Collapsed;
        arrow = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 14,2 L 24,25 L 14,20 L 4,25 Z"),
            Width = 28,
            Height = 28,
            HorizontalAlignment = HorizontalAlignment.Center,
            RenderTransformOrigin = new Point(.5, .5),
            RenderTransform = new RotateTransform(),
            IsHitTestVisible = false
        };
        distance = new TextBlock
        {
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(220, 226, 235)),
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0)
        };
        Children.Add(arrow);
        Children.Add(distance);
    }

    public void Update(PetDirection? direction, DateTimeOffset now, Brush accent)
    {
        double? bearing = direction?.Bearing(now);
        bool hasDistance = direction?.HasDistance(now) == true;
        if (direction == null || !hasDistance && !bearing.HasValue)
        {
            Visibility = Visibility.Collapsed;
            arrow.Visibility = Visibility.Collapsed;
            distance.Text = "";
            ((RotateTransform)arrow.RenderTransform).Angle = 0;
            ToolTip = null;
            AutomationProperties.SetName(this, "");
            return;
        }

        Visibility = Visibility.Visible;
        arrow.Visibility = bearing.HasValue ? Visibility.Visible : Visibility.Collapsed;
        arrow.Fill = accent;
        ((RotateTransform)arrow.RenderTransform).Angle = bearing ?? 0;
        distance.Visibility = hasDistance || direction.NorthUp ? Visibility.Visible : Visibility.Collapsed;
        distance.Text = hasDistance ? (direction.DistanceMetres < 1 ? "<1 m" : Math.Round(direction.DistanceMetres!.Value, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + " m") : direction.NorthUp ? "N up" : "";
        string description = (hasDistance ? "Nearest observed mob: " + distance.Text + ". " : direction.SkillBased || direction.NorthUp ? "Nearest observed mob. " : "Nearest matched mob on the minimap. ") + (bearing.HasValue ? direction.NorthUp ? "Map direction from your position: up is north, right is east. Turning does not rotate the arrow. Stale positions hide it." : direction.SkillBased ? "Experimental arrow relative to your last skill facing; up means ahead. Expires after two seconds." : "Arrow relative to your character; up means ahead." : "Direction unavailable.");
        ToolTip = description;
        AutomationProperties.SetName(this, description);
    }
}
