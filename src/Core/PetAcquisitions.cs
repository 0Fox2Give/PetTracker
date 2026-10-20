using System.Text.Json;

namespace PetTracker;

public sealed record PetAcquisition(
    int PetId,
    string Kind,
    string Instructions,
    string Window,
    string SourceUrl,
    string CheckedOn,
    string[] Aliases,
    DateOnly? AnnouncedEnd = null,
    string SourceLabel = "Official source",
    string SourceName = "Official notice")
{
    public string Availability(DateOnly today) => AnnouncedEnd is not { } end ? "" : today > end ? "Announced earning/sale window has passed. Check the notice for changes." : "Check the linked notice and in-game availability.";
}

public static class PetAcquisitions
{
    public static IReadOnlyList<PetAcquisition> Entries { get; } = Load();

    static PetAcquisition[] Load()
    {
        using var stream = typeof(Catalog).Assembly.GetManifestResourceStream("PetTracker.PetAcquisitions.json")!;
        return JsonSerializer.Deserialize<PetAcquisition[]>(stream)!;
    }

    public static PetAcquisition? Find(int id) => Entries.FirstOrDefault(p => p.PetId == id);
}
