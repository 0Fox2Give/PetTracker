using System.Runtime.InteropServices;
namespace PetTracker;

static class StartupCommandChecks
{
    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr CommandLineToArgvW(string command, out int count);
    [DllImport("kernel32.dll")]
    static extern IntPtr LocalFree(IntPtr memory);
    static string[] Parse(string command)
    {
        var memory = CommandLineToArgvW(command, out int count);
        if (memory == IntPtr.Zero)
            throw new System.ComponentModel.Win32Exception();
        try
        {
            return Enumerable.Range(0, count).Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(memory, i * IntPtr.Size))!).ToArray();
        }
        finally
        {
            LocalFree(memory);
        }
    }
    public static void Run(Action<bool, string> check)
    {
        const string executable = @"C:\Users\Test Player\Programs\Pet Tracker\PetTracker.exe";
        check(StartupCommand.IsLegacyEntry(StartupCommand.Build(executable, @"C:\Data")), "Retirement recognizes the app's legacy Windows startup command");
        foreach (string other in new[] { "", "garbage", StartupCommand.Build(@"C:\Tools\Other.exe", @"C:\Data"), StartupCommand.Build("PetTracker.exe", @"C:\Data"), "\"" + executable + "\" --custom" })
            check(!StartupCommand.IsLegacyEntry(other), "Retirement leaves unrelated or malformed startup commands alone: " + other);
        var old = new AppSettings { StartWithWindows = true, StartMinimized = true };
        old.Normalize();
        check(!old.StartWithWindows && !old.StartMinimized, "Obsolete startup preferences cannot hide or auto-start the new app");
        var overlayReset = SettingsPatch.Apply(new AppSettings { ShareAllCharacters = true, PinnedPetIds = new() { 1050 }, BlackMarketAlerts = false, InactiveSeconds = 60, Scale = 1.4, GameOnly = true }, SettingsSections.Defaults("Overlay"));
        check(overlayReset.ShareAllCharacters && overlayReset.IsPinned(1050) && !overlayReset.BlackMarketAlerts && overlayReset.InactiveSeconds == 60 && overlayReset.Scale == 1 && !overlayReset.GameOnly, "Overlay reset preserves accounts, pins and tracking choices");
        foreach (string data in new[] { @"C:\Users\Test Player\Pet Tracker", @"C:\Users\Test Player\Pet Tracker\", @"C:\", @"\\server\Player Data\Pet Tracker\" })
        {
            var actual = Parse(StartupCommand.Build(executable, data));
            check(actual.SequenceEqual(new[] { executable, "--minimized", "--data", data }), "Windows startup parsing preserves the executable and exact data directory: " + data);
        }
    }
}
