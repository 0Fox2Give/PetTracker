using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Input;

namespace PetTracker;

public sealed class ShortcutRecorder : StackPanel
{
    readonly Button record, clear, cancel;
    readonly TextBlock hint = Ui.Text("", 12, Ui.Muted);
    readonly Action<TrackerShortcut> save;
    readonly Action<bool> recordingChanged;
    TrackerShortcut current;
    public bool Recording { get; private set; }

    public ShortcutRecorder(Action<TrackerShortcut> save, Action<bool> recordingChanged)
    {
        this.save = save;
        this.recordingChanged = recordingChanged;
        var buttons = new WrapPanel();
        record = Ui.Button("Disabled", BeginCapture);
        record.MinWidth = 150;
        record.Margin = new Thickness(0, 0, 8, 0);
        System.Windows.Automation.AutomationProperties.SetAutomationId(record, "Tracker shortcut");
        System.Windows.Automation.AutomationProperties.SetName(record, "Set tracker shortcut");
        clear = Ui.Button("Clear", () => { CancelCapture(); save(default); });
        cancel = Ui.Button("Cancel", CancelCapture);
        clear.Margin = new Thickness(0, 0, 8, 0);
        buttons.Children.Add(record);
        buttons.Children.Add(clear);
        buttons.Children.Add(cancel);
        Children.Add(buttons);
        hint.TextWrapping = TextWrapping.Wrap;
        hint.Margin = new Thickness(0, 6, 0, 0);
        Children.Add(hint);
        record.PreviewKeyDown += (s, e) =>
        {
            if (!Recording)
                return;
            e.Handled = true;
            if (!e.IsRepeat)
                Capture(e.Key == Key.System ? e.SystemKey : e.Key, Keyboard.Modifiers);
        };
        record.LostKeyboardFocus += (s, e) => CancelCapture();
        Unloaded += (s, e) => CancelCapture();
        IsVisibleChanged += (s, e) => { if (!IsVisible) CancelCapture(); };
        Update(default);
    }

    public void Update(TrackerShortcut shortcut)
    {
        current = shortcut;
        if (!Recording)
        {
            record.Content = Label(shortcut);
            hint.Text = "Click to set a key, with any combination of Ctrl, Alt or Shift.";
        }
        clear.Visibility = shortcut.Key == 0 ? Visibility.Collapsed : Visibility.Visible;
        cancel.Visibility = Recording ? Visibility.Visible : Visibility.Collapsed;
    }

    public void BeginCapture()
    {
        if (Recording)
            return;
        Recording = true;
        recordingChanged(true);
        record.Content = "Press your shortcut…";
        hint.Text = "Press your keys together. Esc cancels.";
        cancel.Visibility = Visibility.Visible;
        record.Focus();
    }

    public void CancelCapture()
    {
        if (!Recording)
            return;
        Recording = false;
        recordingChanged(false);
        Update(current);
    }

    public void Capture(Key key, ModifierKeys modifiers)
    {
        if (!Recording)
            return;
        if (key == Key.Escape && modifiers == ModifierKeys.None)
        {
            CancelCapture();
            return;
        }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;
        var shortcut = new TrackerShortcut(KeyInterop.VirtualKeyFromKey(key), (int)modifiers);
        if (!shortcut.IsValid)
        {
            hint.Text = key == Key.F12 ? "Windows reserves F12. Choose another key." : "That key cannot be used. Try another combination.";
            return;
        }
        CancelCapture();
        save(shortcut);
    }

    public static string Label(TrackerShortcut shortcut)
    {
        if (shortcut.Key == 0)
            return "Disabled";
        var parts = new List<string>();
        if ((shortcut.Modifiers & 2) != 0) parts.Add("Ctrl");
        if ((shortcut.Modifiers & 1) != 0) parts.Add("Alt");
        if ((shortcut.Modifiers & 4) != 0) parts.Add("Shift");
        if ((shortcut.Modifiers & 8) != 0) parts.Add("Win");
        uint scan = MapVirtualKey((uint)shortcut.Key, 0);
        int extended = shortcut.Key is 0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or 0x2d or 0x2e or 0x6f or 0x90 ? 1 << 24 : 0;
        var name = new StringBuilder(64);
        GetKeyNameText((int)(scan << 16) | extended, name, name.Capacity);
        parts.Add(name.Length > 0 ? name.ToString() : KeyInterop.KeyFromVirtualKey(shortcut.Key).ToString());
        return string.Join(" + ", parts);
    }

    [DllImport("user32.dll")]
    static extern uint MapVirtualKey(uint code, uint type);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetKeyNameText(int parameter, StringBuilder text, int size);
}
