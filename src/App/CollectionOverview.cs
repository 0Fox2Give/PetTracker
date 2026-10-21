using System.Windows.Controls.Primitives;

namespace PetTracker;

// A compact collection summary doubles as the family selector. Counts always
// cover the supported catalog, independent of the active search and filters.
public sealed class CollectionOverview : UniformGrid
{
    readonly List<(string Family, ToggleButton Button, TextBlock Count)> cards = new();
    public string Family { get; private set; } = "";
    public event Action? Changed;

    public CollectionOverview()
    {
        Rows = 1;
        Columns = 6;
        Margin = new Thickness(0, 0, -6, 14);
        foreach (string family in new[] { "" }.Concat(PetCollection.Families))
        {
            var content = new StackPanel();
            var label = Ui.Text(family.Length == 0 ? "All pets" : family, 12);
            label.FontWeight = FontWeights.SemiBold;
            content.Children.Add(label);
            var count = Ui.Text("", 10, Ui.Muted);
            count.Margin = new Thickness(0, 5, 0, 0);
            content.Children.Add(count);
            var button = new ToggleButton
            {
                Content = content,
                Margin = new Thickness(0, 0, 6, 0),
                Padding = new Thickness(10, 9, 6, 9),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                IsChecked = family.Length == 0
            };
            button.SetResourceReference(StyleProperty, "CollectionFamily");
            System.Windows.Automation.AutomationProperties.SetAutomationId(button, "CollectionFamily" + (family.Length == 0 ? "All" : family));
            button.Click += (_, _) => Select(family);
            cards.Add((family, button, count));
            Children.Add(button);
        }
    }

    public void Select(string family)
    {
        bool changed = Family != family;
        Family = family;
        foreach (var card in cards)
            card.Button.IsChecked = card.Family == Family;
        if (changed)
            Changed?.Invoke();
    }

    public void SetCompact(bool compact)
    {
        foreach (var card in cards)
            card.Count.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
    }

    public void Refresh(Profile profile)
    {
        var entries = PetFinder.Search("", profile).ToArray();
        foreach (var card in cards)
        {
            var summary = PetCollection.Summarize(entries.Where(e => card.Family.Length == 0 || PetFamily.Label(e.Pet.Id) == card.Family), profile);
            card.Count.Text = $"{summary.Collected} / {summary.Total} collected";
            card.Button.ToolTip = $"{summary.Collected} collected · {summary.Maxed} maxed · {summary.Unknown} unknown rank\nSupported pets in the selected collection. Counts include saved progress.";
            System.Windows.Automation.AutomationProperties.SetName(card.Button, (card.Family.Length == 0 ? "All pets" : card.Family) + ": " + card.Count.Text);
        }
    }
}
