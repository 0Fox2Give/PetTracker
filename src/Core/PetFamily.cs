namespace PetTracker;
public static class PetFamily
{
    static readonly IReadOnlyDictionary<int, string> labels = Catalog.Entries.ToDictionary(p => p.Id, p => p.Category switch
    {
        "Intellect" => "Cogni",
        "Feral" => "Fera",
        "Nature" => "Natura",
        "Trans" => "Varian",
        "Special" => "Special",
        _ => ""
    });
    public static string Label(int petId) => labels.GetValueOrDefault(petId, "");
}
