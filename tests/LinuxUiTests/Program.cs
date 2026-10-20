using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PetTracker;
using PetTracker.Linux;

string root = Path.GetFullPath(args[0]);
Directory.CreateDirectory(root);
AppBuilder.Configure<App>().UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
int checks = 0;
void Check(bool success, string message)
{
    checks++;
    if (!success)
        throw new InvalidOperationException(message);
}
string Text(Control control) => string.Join("\n", control.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
void Save(Window window, string file)
{
    Dispatcher.UIThread.RunJobs();
    using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No rendered frame");
    frame.Save(Path.Combine(root, file), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
}

var connection = new FakeConnection();
var window = new TrackerWindow(connection);
window.Show();
Dispatcher.UIThread.RunJobs();
Check(Text(window).Contains("5/25"), "Merchant shows live collection progress before purchase");
Check(Text(window).Contains("80,000 Kina"), "Merchant shows observed price");
Save(window, "merchant.png");

var nearby = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Nearby"));
nearby.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Dispatcher.UIThread.RunJobs();
Check(connection.View.Settings.TrackerMode == "Nearby", "Nearby button patches the worker setting");
connection.Reward();
window.Poll().GetAwaiter().GetResult();
Dispatcher.UIThread.RunJobs();
Check(Text(window).Contains("10/25"), "Merchant progress updates while Nearby is selected");
Check(!Text(window).Contains("80,000 Kina"), "Updated merchant row no longer invites another purchase");
Save(window, "merchant-updated.png");

connection.Fail = true;
window.Poll().GetAwaiter().GetResult();
Dispatcher.UIThread.RunJobs();
Check(window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.IsVisible && t.Text == "Worker connection lost"), "Connection loss remains visible after a live snapshot");
Check(!Text(window).Contains("Black Cloud Merchant"), "Connection loss clears stale merchant advice");
connection.Fail = false;
window.Poll().GetAwaiter().GetResult();
Dispatcher.UIThread.RunJobs();
Check(Text(window).Contains("10/25"), "Recovery renders the next verified worker snapshot");

var finder = new FinderWindow(_ => Task.CompletedTask);
finder.Accept(connection.View);
finder.Show();
Dispatcher.UIThread.RunJobs();
var search = finder.GetVisualDescendants().OfType<TextBox>().Single();
search.Text = "Ancient Spirit";
Dispatcher.UIThread.RunJobs();
Check(Text(finder).Contains("Ancient Spirit") && !Text(finder).Contains("Mutated Insectoid Valka"), "Finder filters by pet name");
Check(search.Bounds.Height <= 40, "Search input remains compact");
Save(finder, "finder.png");
finder.Close();
window.Close();
Dispatcher.UIThread.RunJobs();
Check(connection.Stopped, "Closing requests orderly worker shutdown");
Console.WriteLine($"{checks} Linux UI checks passed.");

sealed class FakeConnection : ITrackerConnection
{
    public WorkerView View { get; private set; }
    public bool Stopped { get; private set; }
    public bool Fail { get; set; }
    readonly int petId;
    public FakeConnection()
    {
        petId = Catalog.Entries.Single(p => p.Name == "Ancient Spirit").Id;
        var profile = new Profile { Pets = Catalog.Create() };
        var pet = profile.Pets[petId];
        pet.Level = 1;
        pet.LevelVerified = true;
        pet.Souls = 5;
        pet.Goal = 25;
        var now = DateTimeOffset.UtcNow;
        View = new(1, profile, true, "Live", "", "Preview", new(), "preview", new(), null, now,
            1, 0, 0, 0, new AppSettings { BlackMarketAlerts = true },
            Market: new[] { new MarketStock(104, now.AddSeconds(-3), now.AddMinutes(30), new[] { new MarketOffer(1, 1, petId, 80000) }) },
            CollectionSynced: true);
    }
    public Task<WorkerView> Send(Command command)
    {
        if (Fail)
            throw new IOException("Worker connection lost");
        if (command.Patch != null)
            View = View with { Settings = SettingsPatch.Apply(View.Settings, command.Patch) };
        return Task.FromResult(View);
    }
    public Task Stop() { Stopped = true; return Task.CompletedTask; }
    public void Reward()
    {
        View.Profile.Pets[petId].Souls = 10;
        var now = DateTimeOffset.UtcNow;
        View = View with { Recent = new() { new RecentPetActivity(petId, null, "", now, SoulRewardUtc: now) } };
    }
}
