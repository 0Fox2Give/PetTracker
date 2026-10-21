using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace PetTracker;

public sealed class GlobalShortcut : IDisposable
{
    readonly HwndSource source;
    readonly Action invoke;
    readonly Action<string> reportError;
    TrackerShortcut current;
    bool suspended, registered, disposed;

    public GlobalShortcut(HwndSource source, Action invoke, Action<string> reportError)
    {
        this.source = source;
        this.invoke = invoke;
        this.reportError = reportError;
        source.AddHook(Message);
    }

    public void Update(TrackerShortcut shortcut, bool suspend = false)
    {
        if (disposed)
            return;
        suspended = suspend;
        var requested = suspend || shortcut.Key == 0 ? default : shortcut;
        if (current == requested)
            return;
        if (registered)
            UnregisterHotKey(source.Handle, 1);
        registered = false;
        current = requested;
        if (current.Key != 0)
            registered = current.IsValid && RegisterHotKey(source.Handle, 1, 0x4000 | (uint)current.Modifiers, (uint)current.Key);
        reportError(current.Key != 0 && !registered ? "Shortcut unavailable. Choose another combination." : "");
    }

    IntPtr Message(IntPtr window, int message, IntPtr id, IntPtr data, ref bool handled)
    {
        long chord = data.ToInt64();
        if (message == 0x312 && id == (IntPtr)1 && registered && !suspended
            && (chord & 0xffff) == current.Modifiers && ((chord >> 16) & 0xffff) == current.Key)
        {
            invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        if (registered)
            UnregisterHotKey(source.Handle, 1);
        registered = false;
        source.RemoveHook(Message);
    }

    [DllImport("user32.dll")]
    static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    static extern bool UnregisterHotKey(IntPtr window, int id);
}
