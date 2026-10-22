using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PetTracker;

static class FarmingUiChecks
{
    static IEnumerable<DependencyObject> Children(DependencyObject root)
    {
        if (root is Popup popup && popup.Child != null)
        {
            yield return popup.Child;
            foreach (var nested in Children(popup.Child))
                yield return nested;
        }
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Children(child))
                yield return nested;
        }
    }
    static T Control<T>(Window window, string id) where T : DependencyObject => Children(window).OfType<T>().Single(c => AutomationProperties.GetAutomationId(c) == id);
    static void Click(Window window, string text) => Children(window).OfType<Button>().Single(b => Equals(b.Content, text)).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    public static void Run(Action<bool, string> check, string folder, WorkerView source, Action<int> pump, Action<Window> show)
    {
        using var host = new Host(Path.Combine(folder, "farming-fixture"), false);
        var profile = new Profile { Pets = Catalog.Create() };
        foreach (var pet in profile.Pets.Values)
        { pet.Level = 1; pet.LevelVerified = pet.CounterVerified = true; pet.Goal = 25; pet.Souls = 12; }
        var now = DateTimeOffset.UtcNow;
        var view = source with { Ready = true, Profile = profile, CollectionSynced = true, Settings = new() { ExpireInactive = false }, Market = Array.Empty<MarketStock>(), Recent = new() { new(1002, null, "", now), new(1004, null, "", now) }, Farming = new(now.AddMinutes(-24), false, new[] { new FarmingSessionRow(1002, 18, 1), new FarmingSessionRow(1004, 12, 0), new FarmingSessionRow(1007, 7, 0) }) };
        host.Preview(view);
        var mapWindow = new PetFinderWindow(host, 1002);
        show(mapWindow);
        // Register the offscreen finder before exercising the real host entry point.
        typeof(Host).GetField("finder", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(host, mapWindow);
        int windowCount = Application.Current.Windows.Count;
        mapWindow.WindowState = WindowState.Minimized;
        host.OpenFarmingMap(1002);
        pump(60);
        var tabs = Control<TabControl>(mapWindow, "FinderViews");
        check(Application.Current.Windows.Count == windowCount && mapWindow.WindowState == WindowState.Normal && Equals(((TabItem)tabs.SelectedItem).Header, "Map"), "Overlay map entry restores and reuses the finder instead of opening another window");
        var canvas = Control<FarmingMapCanvas>(mapWindow, "FarmingMap");
        check(canvas.PointCount == FarmingAtlas.Regions[0].Points.Count(p => p.PetId == 1002), "Map selects only the requested pet's points");
        check(canvas.HasCompleteMap, "All map tiles load from the built output");
        var missingMap = new FarmingMapCanvas(Path.Combine(folder, "no-maps"));
        missingMap.SetRegion(FarmingAtlas.Regions[0]);
        check(!missingMap.HasCompleteMap, "Missing map tiles cannot look like valid empty terrain");
        Program.Save(mapWindow, Path.Combine(folder, "farming-map-verteron.png"), 1100, 780);
        Click(mapWindow, "Fit spawns");
        pump(30);
        check(canvas.Zoom > 1 && canvas.Zoom <= 8, "Fit-spawns frames the selected markers");
        double zoom = canvas.Zoom;
        Click(mapWindow, "−");
        check(canvas.Zoom < zoom, "Zoom-out button changes the map viewport");
        var center = canvas.Center;
        canvas.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(canvas), Environment.TickCount, Key.Right) { RoutedEvent = Keyboard.KeyDownEvent });
        check(canvas.Center.X > center.X, "Keyboard pan moves the map without changing points");
        var list = Control<ListBox>(mapWindow, "PetFinderResults");
        var selectedPet = list.SelectedItem;
        center = canvas.Center;
        double beforeTabZoom = canvas.Zoom;
        tabs.SelectedIndex = 0;
        pump(20);
        tabs.SelectedIndex = 1;
        pump(20);
        check(ReferenceEquals(selectedPet, list.SelectedItem) && canvas.Center == center && canvas.Zoom == beforeTabZoom, "Switching details and map preserves selection and viewport");
        host.Preview(host.View!);
        pump(20);
        check(canvas.Center == center && canvas.Zoom == beforeTabZoom, "Live collection polling leaves the map viewport alone");
        Program.Save(mapWindow, Path.Combine(folder, "farming-map-zoomed.png"), 1100, 780);
        Click(mapWindow, "Reset");
        check(canvas.Zoom < zoom && canvas.Center.X is > 0 and < 1, "Reset frames the whole region");
        Control<ComboBox>(mapWindow, "CollectionRegion").SelectedItem = "Altgard";
        pump(40);
        check(list.Items.Count == PetCollection.Select(PetFinder.Search("", profile), profile, new(Region: "Altgard"), true).Count(), "Region filter is shared by the collection and map");
        int altgardPet = FarmingAtlas.Regions[1].Points.First().PetId;
        mapWindow.SelectPet(altgardPet);
        Control<ComboBox>(mapWindow, "CollectionRegion").SelectedItem = "Altgard";
        pump(30);
        check(canvas.PointCount == FarmingAtlas.Regions[1].Points.Count(p => p.PetId == altgardPet) && ((SpawnRegion)Control<ComboBox>(mapWindow, "MapRegion").SelectedItem).Name == "Altgard", "Selecting a different pet and region replaces the markers and tiles");
        var selectedMapRow = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(list.SelectedItem);
        double selectedMapTop = selectedMapRow.TransformToAncestor(list).Transform(new Point()).Y;
        check(selectedMapTop >= -1 && selectedMapTop + selectedMapRow.ActualHeight <= list.ActualHeight + 1, "Region changes keep the mapped pet visible in the collection");
        Program.Save(mapWindow, Path.Combine(folder, "farming-map-altgard.png"), 1100, 780);
        Control<TextBox>(mapWindow, "PetFinderSearch").Text = "no such pet 123";
        pump(20);
        check(list.Items.Count == 0 && canvas.PointCount == 0 && !canvas.IsVisible, "Empty shared search clears old map markers");
        Program.Save(mapWindow, Path.Combine(folder, "farming-map-empty.png"), 1000, 740);
        var collectionRegion = Control<ComboBox>(mapWindow, "CollectionRegion");
        string unsupportedRegion = collectionRegion.Items.Cast<string>().First(r => r != "All regions" && !FarmingAtlas.Regions.Any(m => m.Name == r));
        Control<TextBox>(mapWindow, "PetFinderSearch").Clear();
        collectionRegion.SelectedItem = unsupportedRegion;
        pump(20);
        check(list.Items.Count > 0 && canvas.PointCount == 0 && !canvas.IsVisible, "Unsupported regions never display another region's map or dots");
        Program.Save(mapWindow, Path.Combine(folder, "farming-map-unsupported-region.png"), 1100, 780);
        collectionRegion.SelectedItem = "Altgard";
        Control<TextBox>(mapWindow, "PetFinderSearch").Text = "spirit";
        Control<ToggleButton>(mapWindow, "CollectionFamilyNatura").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        pump(20);
        check(list.Items.Count > 0 && list.Items.Count == PetCollection.Select(PetFinder.Search("spirit", profile), profile, new("Natura", Region: "Altgard"), true).Count(), "Map uses the same search and family selection as Details");
        Control<ComboBox>(mapWindow, "CollectionRank").SelectedIndex = (int)CollectionRankFilter.HideMaxed;
        Click(mapWindow, "Saved filters");
        Control<TextBox>(mapWindow, "FinderPresetName").Text = "Map spirits";
        Click(mapWindow, "Save filter");
        pump(20);
        Click(mapWindow, "Clear filters");
        Control<ComboBox>(mapWindow, "FinderPresets").SelectedItem = "Map spirits";
        pump(20);
        check(Control<ComboBox>(mapWindow, "CollectionRank").SelectedIndex == (int)CollectionRankFilter.HideMaxed && Control<TextBox>(mapWindow, "PetFinderSearch").Text == "spirit" && tabs.SelectedIndex == 1 && canvas.PointCount > 0, "Saved filters restore shared map search, rank and region without changing views");
        Click(mapWindow, "Delete");
        pump(20);
        Control<Popup>(mapWindow, "SavedFiltersPopup").IsOpen = false;
        mapWindow.Width = 720;
        mapWindow.Height = 500;
        pump(40);
        Program.Save(mapWindow, Path.Combine(folder, "farming-map-small.png"), 720, 500);
        check(canvas.ActualWidth >= 250 && canvas.ActualHeight >= 90, $"Map retains a usable viewport at minimum window size ({canvas.ActualWidth:0} × {canvas.ActualHeight:0})");
        var compactFilters = Control<Button>(mapWindow, "CompactFilters");
        check(Equals(compactFilters.Content, "Filters · 3"), "Compact filter button shows how many hidden choices are active");
        compactFilters.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        pump(30);
        var compactPopup = Control<Popup>(mapWindow, "CompactFiltersPopup");
        check(compactPopup.IsOpen && Control<ComboBox>(mapWindow, "CollectionRank").IsVisible, "Compact finder keeps rank, family and region filters reachable");
        Control<ComboBox>(mapWindow, "CollectionRank").SelectedIndex = (int)CollectionRankFilter.RankOne;
        pump(20);
        check(list.Items.Count > 0 && canvas.PointCount > 0 && tabs.SelectedIndex == 1, "Changing a compact popup filter updates the shared map selection");
        var compactPanel = (FrameworkElement)compactPopup.Child;
        var compactImage = new RenderTargetBitmap((int)Math.Ceiling(compactPanel.ActualWidth), (int)Math.Ceiling(compactPanel.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        compactImage.Render(compactPanel);
        var compactEncoder = new PngBitmapEncoder();
        compactEncoder.Frames.Add(BitmapFrame.Create(compactImage));
        using (var output = File.Create(Path.Combine(folder, "farming-compact-filters.png"))) compactEncoder.Save(output);
        mapWindow.Height = 780;
        pump(30);
        check(!compactPopup.IsOpen && Control<ComboBox>(mapWindow, "CollectionRank").IsVisible && compactFilters.Visibility == Visibility.Collapsed, "Growing the window restores inline filters without leaving the popup open");
        mapWindow.Height = 500;
        pump(30);
        var unmapped = PetFinder.Search("", profile).First(p => !FarmingAtlas.Regions.Any(r => r.Points.Any(s => s.PetId == p.Pet.Id)));
        mapWindow.SelectPet(unmapped.Pet.Id);
        pump(20);
        check(canvas.PointCount == 0 && !canvas.IsVisible && Control<TextBlock>(mapWindow, "MapEmptyState").Text.Contains(unmapped.Label), "Pets without mapped spawns do not retain another pet's dots");
        Program.Save(mapWindow, Path.Combine(folder, "farming-map-unmapped.png"), 720, 500);
        mapWindow.SelectPet(1002);
        tabs.SelectedIndex = 0;
        pump(20);
        Click(mapWindow, "Verteron · View map");
        pump(20);
        check(tabs.SelectedIndex == 1 && canvas.PointCount > 0 && Equals(Control<ComboBox>(mapWindow, "CollectionRegion").SelectedItem, "Verteron"), "Location link opens the selected region inside the finder");
        mapWindow.Close();
        typeof(Host).GetField("finder", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(host, null);
        var session = new FarmingSessionWindow(host);
        show(session);
        pump(30);
        check(Children(session).OfType<TextBlock>().Any(t => t.Text == "37 souls · 1 pet ranked up"), "Session window shows observed reward totals");
        Program.Save(session, Path.Combine(folder, "farming-session.png"), 550, 560);
        var renamed = JsonSerializer.Deserialize<Profile>(JsonSerializer.Serialize(host.View!.Profile))!;
        renamed.Pets[1002].DisplayName = "My Krall";
        host.Preview(host.View with { Profile = renamed, Farming = host.View.Farming! with { Incomplete = true } });
        pump(20);
        check(Children(session).OfType<TextBlock>().Any(t => t.Text == "My Krall"), "Session refresh shows renamed pets even when reward totals stay unchanged");
        check(Children(session).OfType<TextBlock>().Any(t => t.Text.StartsWith("Some rewards may be missing")), "Interrupted sessions show an explicit incomplete-total notice");
        Program.Save(session, Path.Combine(folder, "farming-session-interrupted.png"), 550, 560);
        host.Preview(host.View with { Profile = profile });
        Click(session, "Reset session");
        pump(30);
        check(host.View!.Farming!.Souls == 0 && host.View.Recent.Count == 2 && host.View.Profile.Pets[1002].Souls == 12, "UI session reset preserves collection and activity");
        check(Children(session).OfType<TextBlock>().Any(t => t.Text == "No soul rewards yet."), "Reset clears session rows");
        session.Close();

        var finder = new PetFinderWindow(host, 1002);
        show(finder);
        pump(40);
        Control<TextBox>(finder, "PetFinderSearch").Text = "spirit";
        Control<ComboBox>(finder, "CollectionRegion").SelectedItem = "Verteron";
        Click(finder, "Saved filters");
        pump(20);
        var presets = Control<ComboBox>(finder, "FinderPresets");
        var nameInput = Control<TextBox>(finder, "FinderPresetName");
        check(nameInput.IsEnabled && !nameInput.IsReadOnly && nameInput.Template.FindName("PART_ContentHost", nameInput) != null, "Preset name uses a working text input");
        check(((FrameworkElement)nameInput.Template.FindName("PART_ContentHost", nameInput)).ActualHeight >= 20, "Preset text has enough height to render inside its padding");
        nameInput.Text = "Spirit farm";
        pump(30);
        var popupContent = (FrameworkElement)Control<Popup>(finder, "SavedFiltersPopup").Child;
        var popupImage = new RenderTargetBitmap((int)Math.Ceiling(popupContent.ActualWidth), (int)Math.Ceiling(popupContent.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        popupImage.Render(popupContent);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(popupImage));
        using (var output = File.Create(Path.Combine(folder, "farming-saved-filters.png"))) encoder.Save(output);
        Click(finder, "Save filter");
        pump(30);
        check(host.PetOverlayPreferences.FinderPresets.Single().Query == "spirit", "Save filter stores active search");
        Click(finder, "Clear filters");
        check(presets.SelectedItem == null, "Editing filters clears the active preset so it can be selected again");
        presets.IsDropDownOpen = true;
        pump(20);
        check(Control<Popup>(finder, "SavedFiltersPopup").IsOpen, "Saved filter selector can open inside its popup");
        presets.SelectedItem = "Spirit farm";
        presets.IsDropDownOpen = false;
        pump(30);
        check(Control<TextBox>(finder, "PetFinderSearch").Text == "spirit" && Equals(Control<ComboBox>(finder, "CollectionRegion").SelectedItem, "Verteron"), "Selecting saved filter restores all choices");
        Click(finder, "Delete");
        pump(20);
        check(host.PetOverlayPreferences.FinderPresets.Count == 0, "Delete removes selected saved filter");
        Control<Popup>(finder, "SavedFiltersPopup").IsOpen = false;
        finder.SelectPet(1002);
        pump(20);
        Control<ComboBox>(finder, "FarmingGoal").SelectedIndex = 2;
        pump(20);
        check(host.View!.Profile.Pets[1002].TargetRank == 2, "Goal picker updates selected pet only");
        check(Children(finder).OfType<TextBlock>().Any(t => t.Text == "13 souls to Rank 2"), "Finder displays goal remaining souls");
        Program.Save(finder, Path.Combine(folder, "farming-finder.png"), 1000, 740);
        finder.Width = 720;
        finder.Height = 500;
        pump(40);
        Program.Save(finder, Path.Combine(folder, "farming-finder-small.png"), 720, 500);
        var finderList = Control<ListBox>(finder, "PetFinderResults");
        var selectedRow = (ListBoxItem)finderList.ItemContainerGenerator.ContainerFromItem(finderList.SelectedItem);
        double selectedTop = selectedRow.TransformToAncestor(finderList).Transform(new Point()).Y;
        check(selectedTop >= -1 && selectedTop + selectedRow.ActualHeight <= finderList.ActualHeight + 1, "Resizing keeps the selected pet visible in the list");
        finder.Close();

        var notifications = new List<string>();
        host.Notification += notifications.Add;
        var completed = JsonSerializer.Deserialize<Profile>(JsonSerializer.Serialize(host.View.Profile))!;
        completed.Pets[1002].Level = 2;
        completed.Pets[1002].Souls = 0;
        completed.Pets[1002].Goal = 75;
        host.Preview(host.View with { Profile = completed, Increase = new(1002, 2, DateTimeOffset.UtcNow) });
        host.Preview(host.View!);
        check(notifications.Count(n => n.Contains("farming goal reached")) == 1, "Reached goal notifies once across repeated worker polls");
        check(notifications.Count == 1, "Rank-up and goal completion share one notification");
        host.Preview(host.View! with { CollectionSynced = false });
        host.Preview(host.View! with { CollectionSynced = true });
        check(notifications.Count(n => n.Contains("farming goal reached")) == 1, "Reconnection does not repeat completed-goal notifications");
        host.Preview(host.View! with { Settings = new() { Compact = true, ExpireInactive = false } });
        var overlay = host.Overlay!;
        show(overlay);
        pump(30);
        check(Control<ContentControl>(overlay, "PetRow-1002").Height == 64, "Compact overlay reduces persistent row height");
        Program.Save(overlay, Path.Combine(folder, "farming-compact-overlay.png"), 320, (int)Math.Ceiling(overlay.ActualHeight));
        var activity = new RecentPetActivity(1002, null, "", now.AddSeconds(-20.09));
        var appearance = PetHudActivityPolicy.Appearance(activity, new() { Compact = true, InactiveSeconds = 20 }, now);
        check(appearance.Height > 0 && appearance.Height < 64, "Compact rows retain the collapse animation");
    }
}
