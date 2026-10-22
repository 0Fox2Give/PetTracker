namespace PetTracker;

public sealed class FarmingSessionWindow : Window
{
    readonly Host host;
    readonly TextBlock summary = Ui.Text("", 21);
    readonly TextBlock timing = Ui.Text("", 12, Ui.Muted);
    readonly TextBlock notice = Ui.Text("", 12, Ui.Muted);
    readonly StackPanel rows = new();
    readonly Button reset;
    string rendered = "";

    public FarmingSessionWindow(Host host)
    {
        this.host = host;
        Title = "Pet Tracker — This session";
        Width = 550;
        Height = 560;
        MinWidth = 440;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        NativeTheme.Install(this);
        NativeTheme.DarkTitleBar(this);
        var layout = new DockPanel { Margin = new Thickness(24) };
        Content = layout;
        var heading = new StackPanel();
        DockPanel.SetDock(heading, Dock.Top);
        layout.Children.Add(heading);
        heading.Children.Add(new TextBlock { Text = "This session", FontSize = 24, FontWeight = FontWeights.SemiBold });
        summary.Margin = new Thickness(0, 20, 0, 6);
        heading.Children.Add(summary);
        heading.Children.Add(timing);
        notice.Margin = new Thickness(0, 10, 0, 18);
        notice.TextWrapping = TextWrapping.Wrap;
        heading.Children.Add(notice);
        var footer = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom);
        layout.Children.Add(footer);
        reset = Ui.Button("Reset session", async () => await host.Change(new("reset-farming")));
        System.Windows.Automation.AutomationProperties.SetAutomationId(reset, "ResetFarmingSession");
        reset.ToolTip = "Clears these session totals. Your collection, goals and recent pet rows stay unchanged.";
        DockPanel.SetDock(reset, Dock.Right);
        footer.Children.Add(reset);
        footer.Children.Add(new TextBlock { Text = "Starts over when you reconnect\nor switch characters.", Foreground = Ui.Muted, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
        layout.Children.Add(new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        host.Changed += Refresh;
        Closed += (_, _) => host.Changed -= Refresh;
        Refresh();
    }

    void Refresh()
    {
        var session = host.View?.Farming;
        int ranked = session?.RankedPets ?? 0;
        summary.Text = $"{session?.Souls ?? 0:N0} souls · {ranked} pet{(ranked == 1 ? "" : "s")} ranked up";
        timing.Text = session?.StartedUtc is { } start ? $"{Math.Max(0, (int)(DateTimeOffset.UtcNow - start).TotalMinutes)} min since first soul reward" : "Your next soul reward starts the session.";
        notice.Text = session?.Incomplete == true ? "Some rewards may be missing because capture was interrupted. These are the rewards received by the tracker." : "Counts soul rewards received while connected. Kills and login totals are not counted as rewards.";
        reset.IsEnabled = session?.StartedUtc != null;
        string key = host.View?.Selected + ":" + session?.StartedUtc + ":" + string.Join(";", session?.Pets.Select(p => $"{p.PetId}:{p.Souls}:{p.RankUps}:{host.Tracker.Profile.Pets.GetValueOrDefault(p.PetId)?.Name}") ?? Array.Empty<string>());
        if (rendered == key && rows.Children.Count > 0)
            return;
        rendered = key;
        rows.Children.Clear();
        if (session == null || session.Pets.Count == 0)
        { rows.Children.Add(Ui.Text("No soul rewards yet.", 13, Ui.Muted)); return; }
        var portraits = new PetPortraits(host.AssetRoot);
        foreach (var row in session.Pets)
        {
            var line = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            line.Children.Add(new Image { Source = portraits.Get(row.PetId), Width = 38, Height = 38, Margin = new Thickness(0, 0, 12, 0) });
            var total = Ui.Text($"+{row.Souls:N0}", 16);
            total.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(total, Dock.Right);
            line.Children.Add(total);
            var name = host.Tracker.Profile.Pets.TryGetValue(row.PetId, out var pet) ? new PetNames().Resolve(pet) : "Pet " + row.PetId;
            var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            label.Children.Add(Ui.Text(name, 13));
            if (row.RankUps > 0)
                label.Children.Add(Ui.Text($"{row.RankUps} rank increase{(row.RankUps == 1 ? "" : "s")}", 10, Ui.Muted));
            line.Children.Add(label);
            rows.Children.Add(line);
        }
    }
}
