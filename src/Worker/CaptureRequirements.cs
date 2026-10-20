namespace PetTracker;
internal static class CaptureRequirements
{
    public static string Help => OperatingSystem.IsLinux() ? "Capture unavailable. Install libpcap and grant capture permission to PetTracker.Capture. See docs/LINUX.md." : "Capture unavailable. Install Npcap or check capture permissions.";
}
