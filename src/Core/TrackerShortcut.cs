namespace PetTracker;

// Modifier bits match RegisterHotKey: Alt=1, Ctrl=2, Shift=4, Windows=8.
public readonly record struct TrackerShortcut(int Key, int Modifiers)
{
    public bool IsValid => Key is >= 0x08 and <= 0xfe
        && Key is not (0x10 or 0x11 or 0x12 or 0x5b or 0x5c or 0x7b)
        && Key is not (>= 0xa0 and <= 0xa5)
        && Modifiers is >= 0 and <= 15;
}
