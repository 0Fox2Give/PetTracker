using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace PetTracker;

static class CollectionFinderChecks
{
    static IEnumerable<DependencyObject> Children(DependencyObject root)
    {
        if (root is Popup popup && popup.Child != null)
        {
            yield return popup.Child;
            foreach (var child in Children(popup.Child)) yield return child;
        }
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Children(child))
                yield return nested;
        }
    }

    public static void Run(Action<bool, string> check, string folder, WorkerView source, Action<int> pump, Action<Window> show)
    {
        using var host = new Host(Path.Combine(folder, "collection-fixture"), false);
        var profile = new Profile { Pets = Catalog.Create() };
        var entries = PetFinder.Search("", profile).ToArray();
        // Varied synthetic ranks, plus intentionally unknown records.
        foreach (var (entry, index) in entries.Select((e, i) => (e, i)))
        {
            var pet = profile.Pets[entry.Pet.Id];
            if (index % 9 == 0)
                continue;
            pet.Level = index % 4;
            pet.Goal = PetStages.CounterGoal(pet.Level.Value);
            pet.Souls = pet.Goal is int goal ? Math.Max(0, goal - (index % 15 + 1)) : 0;
            pet.LevelVerified = pet.CounterVerified = true;
        }
        var view = source with { Profile = profile, CollectionSynced = true, Market = Array.Empty<MarketStock>(), Recent = new(), Settings = new() };
        host.Preview(view);
        var finder = new PetFinderWindow(host);
        show(finder);
        pump(50);
        T Control<T>(string id) where T : DependencyObject => Children(finder).OfType<T>().Single(c => AutomationProperties.GetAutomationId(c) == id);
        var list = Control<ListBox>("PetFinderResults");
        var family = Control<ToggleButton>("CollectionFamilyFera");
        family.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        pump(30);
        check(list.Items.Count == entries.Count(e => PetFamily.Label(e.Pet.Id) == "Fera"), "Clicking a family card filters the collection");
        var rank = Control<ComboBox>("CollectionRank");
        rank.SelectedIndex = (int)CollectionRankFilter.RankTwo;
        var region = Control<ComboBox>("CollectionRegion");
        region.SelectedItem = "Verteron";
        var sorting = Control<ComboBox>("CollectionSort");
        sorting.SelectedIndex = (int)CollectionSort.ClosestToNextRank;
        pump(30);
        int expected = PetCollection.Select(entries, profile, new("Fera", CollectionRankFilter.RankTwo, "Verteron", CollectionSort.ClosestToNextRank), true).Count();
        check(expected > 0 && list.Items.Count == expected, "Finder combines family, rank and region with next-rank sorting");
        check(Children(finder).OfType<TextBlock>().Any(t => t.Text.Contains("soul") && t.Text.Contains("to Rank 3")), "Verified next-rank distances are visible in filtered rows");
        check(Children(finder).OfType<Button>().First(b => (b.ToolTip?.ToString() ?? "").StartsWith("Show in Map tab · ") || (b.ToolTip?.ToString() ?? "").StartsWith("Open source map · ")).Content?.ToString()?.StartsWith("Verteron") == true, "Selected farming region appears first in pet details");
        Program.Save(finder, Path.Combine(folder, "collection-filtered.png"), 1000, 740);
        var reset = Children(finder).OfType<Button>().Single(b => Equals(b.Content, "Clear filters"));
        reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        pump(30);
        check(list.Items.Count == entries.Length && rank.SelectedIndex == 0 && region.SelectedIndex == 0 && sorting.SelectedIndex == 0 && !reset.IsEnabled, "Clear filters restores the full collection and default ordering");
        Program.Save(finder, Path.Combine(folder, "collection-overview.png"), 1000, 740);
        var selected = list.SelectedItem;
        host.Preview(view);
        pump(30);
        check(ReferenceEquals(selected, list.SelectedItem), "Unchanged collection refresh preserves the selected row");
        rank.SelectedIndex = (int)CollectionRankFilter.RankTwo;
        finder.SelectPet(entries[0].Pet.Id);
        pump(30);
        check(rank.SelectedIndex == 0 && list.SelectedItem != null, "Opening a specific pet clears filters that would conceal it");
        finder.Width = 720;
        finder.Height = 500;
        pump(30);
        Program.Save(finder, Path.Combine(folder, "collection-compact.png"), 720, 500);
        check(Control<TextBox>("PetFinderSearch").ActualHeight <= 34 && list.ActualHeight > 130, "Compact collection keeps a small search field and usable list");
        host.Preview(view with { CollectionSynced = false });
        pump(30);
        check(Control<TextBlock>("CollectionStatus").Text.StartsWith("Saved collection") && !Children(finder).OfType<TextBlock>().Any(t => t.Text.Contains("soul") && t.Text.Contains("to Rank")), "Capture sync loss labels saved data and removes next-rank recommendations");
        rank.SelectedIndex = (int)CollectionRankFilter.Uncollected;
        host.Preview(view with { Profile = new Profile { Pets = Catalog.Create() }, CollectionSynced = false });
        pump(30);
        check(list.Items.Count == 0, "A fresh account does not appear to have every pet missing");
        Program.Save(finder, Path.Combine(folder, "collection-unknown.png"), 720, 500);
        finder.Close();
    }
}
