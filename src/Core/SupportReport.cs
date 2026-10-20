using System.Text.Json;

namespace PetTracker;

// Explicit allowlist: never serialize the worker view, message samples, event
// text or exceptions into a report intended to be shared with another person.
public static class SupportReport
{
    public static string Create(WorkerView view, string version)
    {
        var h = view.Health;
        return JsonSerializer.Serialize(new { ReportFormat = 1, Version = version, RecordedUtc = DateTimeOffset.UtcNow, view.Ready, CharacterDetected = !string.IsNullOrWhiteSpace(view.DetectedCharacter), AccountConfigured = view.Selected != null, view.Devices, AdapterPacketsReceived = view.Received, AdapterPacketsDropped = view.Dropped, GameStreamGaps = view.Gaps, view.LastSync, RecentPets = view.Recent.Count, Counters = h == null ? null : new { h.Payloads, h.Frames, h.Rewards, h.DecoderFaults, h.ActiveInterruptions, h.BackgroundInterruptions, h.RetiredFlows, h.LastPayload, h.LastIdentity, h.LastReward }, Note = "Adapter counters include other network traffic. Idle connection cleanup is not a stream interruption." }, new JsonSerializerOptions { WriteIndented = true });
    }
}
