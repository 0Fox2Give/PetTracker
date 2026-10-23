using System.Text.Json;

namespace PetTracker;

// Explicit allowlist: never serialize the worker view, message samples, event
// text or exceptions into a report intended to be shared with another person.
public static class SupportReport
{
    public static string Create(WorkerView view, string version)
    {
        var h = view.Health;
        var now = DateTimeOffset.UtcNow;
        var stocks = (view.Market ?? Array.Empty<MarketStock>()).Where(s => s.Fresh(now)).ToArray();
        return JsonSerializer.Serialize(new
        {
            ReportFormat = 1,
            Version = version,
            RecordedUtc = now,
            view.Ready,
            view.CollectionSynced,
            CharacterDetected = !string.IsNullOrWhiteSpace(view.DetectedCharacter),
            AccountConfigured = view.Selected != null,
            view.Devices,
            AdapterPacketsReceived = view.Received,
            AdapterPacketsDropped = view.Dropped,
            GameStreamGaps = view.Gaps,
            view.LastSync,
            RecentPets = view.Recent.Count,
            Merchant = new
            {
                Enabled = view.Settings.BlackMarketAlerts,
                FreshStockTabs = stocks.Length,
                MappedOffers = stocks.Sum(s => s.Offers.Count),
                EligiblePets = MarketOverlay.Select(view, now).Count
            },
            Counters = h == null ? null : new
            {
                h.Payloads, h.Frames, h.Rewards, h.DecoderFaults,
                h.ActiveInterruptions, h.BackgroundInterruptions, h.RetiredFlows,
                h.LastPayload, h.LastIdentity, h.LastReward
            },
            Note = "Adapter counters include other network traffic. Idle connection cleanup is not a stream interruption."
        }, new JsonSerializerOptions { WriteIndented = true });
    }
}
