namespace PetTracker;

public sealed record PetPointObservation(int PetId, int Value, DateTimeOffset ObservedUtc);
public static class PetPointDecoder
{
    public static string? SupportedCatalog(int pet) => pet switch
    {
        1066 => "Papis_01",
        1086 => "Fungus_01",
        1012 => "Fungy_01",
        _ => null
    };
}
