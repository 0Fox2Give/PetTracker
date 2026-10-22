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
        var mapWindow = new FarmingMapWindow(host, 1002);
        show(mapWindow);
        pump(60);
        var canvas = Control<FarmingMapCanvas>(mapWindow, "FarmingMap");
        check(canvas.PointCount == FarmingAtlas.Regions[0].Points.Count(p => p.PetId == 1002), "Map selects only the requested pet's points");
        check(canvas.HasCompleteMap, "All map tiles load from the built output");
        var missingMap = new FarmingMapCanvas(Path.Combine(folder, "no-maps"));
        missingMap.SetRegion(FarmingAtlas.Regions[0]);
        check(!missingMap.HasCompleteMap, "Missing map tiles cannot look like valid empty terrain");
        Program.Save(mapWindow, Path.Combine(folder, "farming-map-verteron.png"), 1040, 720);
        Click(mapWindow, "Fit spawns");
        pump(30);
        check(canvas.Zoom > 1 && canvas.Zoom <= 8, "Fit-spawns frames the selected markers");
        double zoom = canvas.Zoom;
        Click(mapWindow, "−");
        check(canvas.Zoom < zoom, "Zoom-out button changes the map viewport");
        var center = canvas.Center;
        canvas.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(canvas), Environment.TickCount, Key.Right) { RoutedEvent = Keyboard.KeyDownEvent });
        check(canvas.Center.X > center.X, "Keyboard pan moves the map without changing points");
        Program.Save(mapWindow, Path.Combine(folder, "farming-map-zoomed.png"), 1040, 720);
        Click(mapWindow, "Reset");
        check(canvas.Zoom < zoom && canvas.Center.X is > 0 and < 1, "Reset frames the whole region");
        Control<ComboBox>(mapWindow, "MapRegion").SelectedIndex = 1;
        pump(40);
        var list = Control<ListBox>(mapWindow, "MapPets");
        check(list.Items.Count == FarmingAtlas.Pets(FarmingAtlas.Regions[1], profile, "", "", false).Count(), "Switching region refreshes supported pet list");
        Program.Save(mapWindow, Path.Combine(folder, "farming-map-altgard.png"), 1040, 720);
        Control<TextBox>(mapWindow, "MapSearch").Text = "no such pet 123";
        pump(20);
        check(list.Items.Count == 0 && canvas.PointCount == 0, "Empty search clears old map markers");
        Control<TextBox>(mapWindow, "MapSearch").Text = "spirit";
        Control<ComboBox>(mapWindow, "MapFamily").SelectedItem = "Natura";
        pump(20);
        check(list.Items.Count > 0 && list.Items.Cast<PetSearchResult>().All(p => PetFamily.Label(p.Pet.Id) == "Natura"), "Map search and family controls combine");
        mapWindow.Width = 760;
        mapWindow.Height = 520;
        pump(30);
        Program.Save(mapWindow, Path.Combine(folder, "farming-map-small.png"), 760, 520);
        mapWindow.Close();

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
