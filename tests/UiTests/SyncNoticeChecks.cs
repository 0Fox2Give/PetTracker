using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace PetTracker;

static class SyncNoticeChecks
{
    static IEnumerable<DependencyObject> Children(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Children(child)) yield return nested;
        }
    }

    public static void Run(Action<bool, string> check, string folder, WorkerView source, Action<int> pump, Action<Window> show)
    {
        using var host = new Host(Path.Combine(folder, "sync-notice-fixture"), false);
        var profile = new Profile { Pets = Catalog.Create() };
        var pet = profile.Pets[1038];
        pet.Level = 2; pet.LevelVerified = true; pet.Souls = 5; pet.Goal = 75;
        var now = DateTimeOffset.UtcNow;
        var view = source with
        {
            Profile = profile,
            Settings = new() { Visible = true, ReducedMotion = true, ExpireInactive = false, TrackerMode = "Nearby" },
            Nearby = new[] { new NearbyPet(1038, "Longbeak Highland Airon", now) },
            Recent = new() { new(1038, null, "", now) },
            CollectionSynced = true
        };
        host.Preview(view);
        var overlay = host.Overlay!;
        show(overlay); pump(40);
        var notice = Children(overlay).OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "CollectionSyncNotice");
        var row = Children(overlay).OfType<ContentControl>().Single(b => AutomationProperties.GetAutomationId(b) == "PetRow-1038");
        check(notice.Visibility == Visibility.Collapsed, "A synchronized tracker has no sync warning");
        pet.ProgressNeedsSync = true;
        view = view with { CollectionSynced = false, Status = "Collection sync needed" };
        host.Preview(view); pump(30);
        check(notice.IsVisible && Children(notice).OfType<TextBlock>().Any(t => t.Text == "Totals paused"), "Lost collection continuity is visible even when Nearby has rows");
        check(Children(row).OfType<TextBlock>().Any(t => t.Text == "5 / 75") && pet.Souls == 5, "A sync warning preserves the last known total instead of inventing rewards");
        check(Children(notice).OfType<TextBlock>().Any(t => t.Text.Contains("character selection")), "The visible warning explains how to resync without requiring a tooltip");
        check(Children(notice).OfType<TextBlock>().All(t => t.ActualWidth <= notice.ActualWidth), "Sync instructions fit the compact overlay");
        Program.Save(overlay, Path.Combine(folder, "sync-paused-nearby.png"), 340, 245);
        view.Settings.TrackerMode = "Recent";
        host.Preview(view); pump(20);
        check(notice.IsVisible, "Recent mode also exposes frozen totals");
        Program.Save(overlay, Path.Combine(folder, "sync-paused-recent.png"), 340, 245);
        pet.ProgressNeedsSync = false; pet.Souls = 7;
        view = view with { CollectionSynced = true, Status = "Live" };
        host.Preview(view); pump(20);
        check(notice.Visibility == Visibility.Collapsed && Children(row).OfType<TextBlock>().Any(t => t.Text == "7 / 75"), "A fresh collection clears the warning and refreshes the existing row");
        check(Children(overlay).OfType<ContentControl>().Any(b => ReferenceEquals(b, row)), "Sync recovery keeps the same pet row attached");
        Program.Save(overlay, Path.Combine(folder, "sync-recovered.png"), 340, 200);
        host.Preview(view with { CollectionSynced = false, Status = "Capture unavailable", Devices = 0 });
        check(notice.Visibility == Visibility.Collapsed, "A capture failure does not give misleading character-login instructions");
        view = view with { Ready = false, CollectionSynced = false };
        host.Preview(view);
        check(notice.Visibility == Visibility.Collapsed, "A disconnected tracker does not mislabel the connection as paused totals");
    }
}
