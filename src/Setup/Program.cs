using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
namespace PetTracker.Setup;

public static class Installer
{
    public static string DefaultLocation => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "PetTracker");
    static string Resolve(string root, string name)
    {
        if (Path.IsPathRooted(name))
            throw new InvalidDataException("The package contains an invalid path.");
        string path = Path.GetFullPath(Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The package contains an invalid path.");
        return path;
    }
    public static void Extract(Stream archive, string target)
    {
        Directory.CreateDirectory(target);
        using var zip = new ZipArchive(archive, ZipArchiveMode.Read, true);
        if (zip.Entries.Count is < 5 or > 1000)
            throw new InvalidDataException("The package is incomplete.");
        long size = 0;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in zip.Entries)
        {
            if (item.FullName.EndsWith('/'))
                continue;
            if (!names.Add(item.FullName) || ((item.ExternalAttributes >> 16) & 0xf000) == 0xa000)
                throw new InvalidDataException("Invalid package entry.");
            size += item.Length;
            if (size > 500000000)
                throw new InvalidDataException("The package is too large.");
            string path = Resolve(target, item.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            item.ExtractToFile(path, false);
        }
        string manifest = Path.Combine(target, "package.json");
        var hashes = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(manifest)) ?? throw new InvalidDataException("The package manifest is empty.");
        if (names.Count != hashes.Count + 1)
            throw new InvalidDataException("Unexpected package content.");
        foreach (var entry in hashes)
        {
            using var file = File.OpenRead(Resolve(target, entry.Key));
            if (!Convert.ToHexString(SHA256.HashData(file)).Equals(entry.Value, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("A package file failed verification.");
        }
        if (!File.Exists(Path.Combine(target, "PetTracker.exe")) || !File.Exists(Path.Combine(target, "PetTracker.Capture.exe")))
            throw new InvalidDataException("The app is missing from the package.");
    }
    public static string Install(string destination)
    {
        destination = Path.GetFullPath(destination);
        string parent = Path.GetDirectoryName(destination) ?? throw new InvalidDataException("Invalid install folder.");
        Directory.CreateDirectory(parent);
        string stage = Path.Combine(parent, "PetTracker-install-" + Guid.NewGuid().ToString("N")), backup = destination + ".previous";
        // A staging folder owned by this invocation is the only recursively deleted path.
        try
        {
            using var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("PetTracker.Payload.zip") ?? throw new InvalidDataException("The install package is missing.");
            using var expected = Assembly.GetExecutingAssembly().GetManifestResourceStream("PetTracker.Payload.sha256")!;
            using var reader = new StreamReader(expected);
            string digest = reader.ReadToEnd().Trim();
            if (Convert.ToHexString(SHA256.HashData(payload)) != digest)
                throw new InvalidDataException("The installer package failed verification.");
            payload.Position = 0;
            Extract(payload, stage);
            foreach (var p in Process.GetProcessesByName("PetTracker").Concat(Process.GetProcessesByName("PetTracker.Capture")))
            {
                using (p)
                {
                    try
                    {
                        string? file = p.MainModule?.FileName;
                        if (file != null && Path.GetDirectoryName(file)!.Equals(destination, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("Choose Quit Pet Tracker from its tray menu, then install again.");
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                        throw new InvalidOperationException("Close Pet Tracker before installing this update.");
                    }
                }
            }
            if (Directory.Exists(backup))
                Directory.Move(backup, backup + "-" + Guid.NewGuid().ToString("N"));
            bool old = Directory.Exists(destination);
            if (old)
                Directory.Move(destination, backup);
            try
            {
                Directory.Move(stage, destination);
            }
            catch
            {
                if (old && !Directory.Exists(destination))
                    Directory.Move(backup, destination);
                throw;
            }
            return destination;
        }
        finally
        {
            if (Directory.Exists(stage))
                Directory.Delete(stage, true);
        }
    }
    public static void Shortcuts(string location, bool desktop)
    {
        Type type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows shortcuts are unavailable.");
        dynamic shell = Activator.CreateInstance(type)!;
        void Link(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            dynamic link = shell.CreateShortcut(path);
            link.TargetPath = Path.Combine(location, "PetTracker.exe");
            link.WorkingDirectory = location;
            link.IconLocation = Path.Combine(location, "PetTracker.ico");
            link.Description = "Aion 2 pet collection tracker";
            link.Save();
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link);
        }
        Link(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Pet Tracker.lnk"));
        if (desktop)
            Link(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Pet Tracker.lnk"));
        System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\PetTracker");
        key.SetValue("DisplayName", "Pet Tracker");
        key.SetValue("DisplayVersion", Assembly.GetExecutingAssembly().GetName().Version!.ToString(3));
        key.SetValue("Publisher", "Pet Tracker");
        key.SetValue("InstallLocation", location);
        key.SetValue("DisplayIcon", Path.Combine(location, "PetTracker.exe"));
        key.SetValue("UninstallString", "\"" + Path.Combine(location, "Uninstall.exe") + "\" --uninstall");
        key.SetValue("NoModify", 1);
        key.SetValue("NoRepair", 1);
    }
    public static void Uninstall()
    {
        string location = DefaultLocation;
        if (MessageBox.Show("Remove Pet Tracker? Your saved collections and settings will be kept.", "Pet Tracker", MessageBoxButton.OKCancel) != MessageBoxResult.OK)
            return;
        var files = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(location, "package.json")))!;
        string? remaining = UninstallPayload.Remove(location, files.Keys.Concat(new[] { "package.json", "Uninstall.exe" }));
        foreach (string link in new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Pet Tracker.lnk"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Pet Tracker.lnk") })
            if (File.Exists(link))
                File.Delete(link);
        using (var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
            run?.DeleteValue("PetTracker", false);
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\PetTracker", false);
        MessageBox.Show("Pet Tracker has been removed. Saved collections were kept." + (remaining == null ? "" : " Some program files could not be deleted and remain in " + remaining + "."), "Pet Tracker");
    }
}
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Contains("--uninstall"))
        {
            string temporary = Path.Combine(Path.GetTempPath(), "PetTracker-uninstall-" + Guid.NewGuid().ToString("N") + ".exe");
            File.Copy(Environment.ProcessPath!, temporary);
            Process.Start(new ProcessStartInfo(temporary) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, ArgumentList = { "--uninstall-run" } });
            return;
        }
        if (args.Contains("--uninstall-run"))
        {
            try
            {
                Installer.Uninstall();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Pet Tracker");
            }
            return;
        }
        if (args.Length == 2 && args[0] == "--extract-test")
        {
            Installer.Install(args[1]);
            return;
        }
        var app = new Application();
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/PetTrackerSetup;component/NativeTheme.xaml", UriKind.Relative) });
        var w = new Window { Title = "Pet Tracker Setup", Width = 520, Height = 470, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = new SolidColorBrush(Color.FromRgb(11, 16, 23)), Foreground = new SolidColorBrush(Color.FromRgb(223, 230, 239)), FontFamily = new FontFamily("Segoe UI") };
        var panel = new StackPanel { Margin = new Thickness(32) };
        w.Content = panel;
        panel.Children.Add(new TextBlock { Text = "Pet Tracker", FontSize = 30, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "Your Aion 2 pet collection, at a glance.", FontSize = 14, Margin = new Thickness(0, 8, 0, 28) });
        panel.Children.Add(new TextBlock { Text = "Install for your Windows account. Your collections stay on this PC.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 20) });
        var desktop = new CheckBox { Content = "Create a desktop shortcut", IsChecked = true, Margin = new Thickness(0, 0, 0, 12), Foreground = w.Foreground };
        var launch = new CheckBox { Content = "Open Pet Tracker after installation", IsChecked = true, Margin = new Thickness(0, 0, 0, 24), Foreground = w.Foreground };
        panel.Children.Add(desktop);
        panel.Children.Add(launch);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        panel.Children.Add(status);
        bool finished = false;
        var button = new Button { Content = "Install Pet Tracker", Padding = new Thickness(18, 10, 18, 10), Background = new SolidColorBrush(Color.FromRgb(217, 178, 76)), Foreground = Brushes.Black, FontWeight = FontWeights.SemiBold };
        panel.Children.Add(button);
        button.Click += async (s, e) =>
        {
            if (finished)
            {
                w.Close();
                return;
            }
            button.IsEnabled = false;
            status.Text = "Installing…";
            try
            {
                string location = await System.Threading.Tasks.Task.Run(() => Installer.Install(Installer.DefaultLocation));
                File.Copy(Environment.ProcessPath!, Path.Combine(location, "Uninstall.exe"), true);
                Installer.Shortcuts(location, desktop.IsChecked == true);
                status.Text = "Installed. Live tracking requires Npcap, installed separately from https://npcap.com/#download. Open Pet Tracker before logging into your character.";
                if (launch.IsChecked == true)
                    Process.Start(new ProcessStartInfo(Path.Combine(location, "PetTracker.exe")) { UseShellExecute = true, WorkingDirectory = location });
                button.Content = "Done";
                button.IsEnabled = true;
                finished = true;
            }
            catch (Exception ex)
            {
                status.Text = ex.Message;
                button.IsEnabled = true;
            }
        };
        app.Run(w);
    }
}
