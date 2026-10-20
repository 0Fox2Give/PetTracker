using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace PetTracker.Linux;

public sealed class FinderWindow : Window
{
    readonly Func<Command, Task> change;
    readonly TextBox search = new() { PlaceholderText = "Search pets or mobs…", Height = 36 };
    readonly StackPanel results = new() { Spacing = 12 };
    readonly TextBlock count = new() { Foreground = Brushes.LightSlateGray };
    WorkerView? view;
    string rendered = "";
    public FinderWindow(Func<Command, Task> change)
    {
        this.change = change;
        Title = "Find a pet";
        Width = 620;
        Height = 700;
        MinWidth = 420;
        Background = Brush.Parse("#0B1118");
        var layout = new DockPanel { Margin = new Thickness(20) };
        var header = new StackPanel { Spacing = 10, Margin = new Thickness(0, 0, 0, 15) };
        header.Children.Add(new TextBlock { Text = "Find a pet", FontSize = 26 });
        header.Children.Add(search);
        header.Children.Add(count);
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);
        layout.Children.Add(new ScrollViewer { Content = results });
        Content = layout;
        search.TextChanged += (_, _) => Refresh();
        Refresh();
    }

    public void Accept(WorkerView current)
    {
        view = current;
        Refresh();
    }

    void Refresh()
    {
        var matches = PetFinder.Search(search.Text ?? "", view?.Profile).ToArray();
        var profile = view?.Profile ?? new Profile { Pets = Catalog.Create() };
        var preferences = view?.Settings ?? new();
        string key = System.Text.Json.JsonSerializer.Serialize(new { Query = search.Text, profile.Pets, Pins = preferences.Pins(), preferences.PinLimit, Connected = view != null });
        if (rendered == key)
            return;
        rendered = key;
        results.Children.Clear();
        count.Text = matches.Length == 1 ? "1 pet" : $"{matches.Length} pets";
        foreach (var match in matches)
        {
            if (!profile.Pets.TryGetValue(match.Pet.Id, out var pet))
                continue;
            var card = new StackPanel { Spacing = 6 };
            card.Children.Add(new PetRow(pet));
            bool pinned = preferences.IsPinned(pet.Id);
            var pin = TrackerWindow.Button(pinned ? "Unpin" : "Pin pet", () => change(new(pinned ? "unpin" : "pin", PetId: pet.Id)));
            pin.IsEnabled = view != null && (pinned || preferences.CanPin(pet.Id));
            card.Children.Add(pin);
            if (match.Creatures.Count > 0)
                card.Children.Add(new TextBlock { Text = string.Join(", ", match.Creatures), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightSlateGray });
            var maps = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var map in match.AvailableMaps.Where(m => PetFinder.AllowedUrl(m.Url)))
            {
                var link = TrackerWindow.Button(map.Region + " ↗", () =>
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(map.Url) { UseShellExecute = true })?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        count.Text = "Could not open map: " + ex.Message;
                    }

                    return Task.CompletedTask;
                });
                link.Margin = new Thickness(0, 0, 6, 0);
                maps.Children.Add(link);
            }

            card.Children.Add(maps);
            if (match.Acquisition is { } acquisition)
                card.Children.Add(new TextBlock { Text = string.Join(", ", acquisition.Aliases), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightSlateGray });
            results.Children.Add(card);
        }
    }
}
