using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace PetTracker;

public static class ShortcutChecks
{
    public static void Run(Action<bool, string> check)
    {
        using var source = new HwndSource(new HwndSourceParameters("Shortcut regression checks") { Width = 0, Height = 0, WindowStyle = 0 });
        string error = "";
        using var shortcut = new GlobalShortcut(source, () => { }, value => error = value);
        // F23 is not used by the shipped presets. Always release test registrations.
        var first = new TrackerShortcut(0x86, 6);
        var changedModifiers = new TrackerShortcut(0x86, 7);
        bool Available(TrackerShortcut value)
        {
            bool registered = RegisterHotKey(source.Handle, 2, (uint)value.Modifiers, (uint)value.Key);
            if (registered) UnregisterHotKey(source.Handle, 2);
            return registered;
        }
        check(Available(first) && Available(changedModifiers), "Isolated shortcut test combinations are available");
        shortcut.Update(first);
        check(error == "" && !Available(first) && Available(changedModifiers), "Native registration uses the exact chosen modifiers");
        shortcut.Update(changedModifiers);
        check(Available(first) && !Available(changedModifiers), "Changing only modifiers releases the old chord and registers the new one");
        shortcut.Update(changedModifiers, true);
        check(Available(changedModifiers), "Recording releases the global chord so the recorder can receive it");
        shortcut.Update(changedModifiers);
        check(!Available(changedModifiers), "Cancelling recording restores the chord");
        shortcut.Update(default);
        check(Available(changedModifiers), "Clearing unregisters the global chord");
        check(RegisterHotKey(source.Handle, 2, 6, 0x86), "Native conflict fixture is registered");
        try
        {
            shortcut.Update(first);
            check(error.Contains("unavailable"), "An actual Windows registration conflict is reported");
        }
        finally { UnregisterHotKey(source.Handle, 2); }
        shortcut.Update(default);
        shortcut.Update(first);
        check(error == "" && !Available(first), "A fresh registration succeeds after the conflict is removed");
        shortcut.Dispose();
        check(Available(first), "Shutdown releases the registered shortcut");
    }

    [DllImport("user32.dll")]
    static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    static extern bool UnregisterHotKey(IntPtr window, int id);
}
