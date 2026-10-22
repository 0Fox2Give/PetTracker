namespace PetTracker;

public sealed record FinderPreset(string Name, string Query = "", string Family = "", int Rank = 0, string Region = "", int Sort = 0, int Source = 0)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Valid => !string.IsNullOrWhiteSpace(Name) && Name.Length <= 40 && Query != null && Query.Length <= 120 &&
        Family is "" or "Cogni" or "Fera" or "Natura" or "Varian" or "Special" && Enum.IsDefined((CollectionRankFilter)Rank) &&
        Sort is >= 0 and <= 1 && Source is >= 0 and <= 2 &&
        (Region == "" || PetFinder.Entries.SelectMany(p => p.AvailableMaps).Any(m => m.Region == Region));
}
