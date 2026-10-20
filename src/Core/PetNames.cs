namespace PetTracker;
public sealed class PetNames
{
    public string Resolve(PetProgress p) => !string.IsNullOrEmpty(p.DisplayName) && p.DisplayName != p.CatalogName ? p.DisplayName : Catalog.Entries.FirstOrDefault(c => c.Id == p.Id)?.Name ?? "Pet #" + p.Id;
}
