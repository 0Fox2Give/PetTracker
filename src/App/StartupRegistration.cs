namespace PetTracker;
internal static class StartupRegistration
{
    public static void RemoveLegacyEntry()
    {
        // Development copies must not change the installed app's registration.
        if (Velopack.Locators.VelopackLocator.Current.AppId != PetTracker.Updates.SignedReleaseSource.PackageId)
            return;
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key?.GetValue("PetTracker")is string command && StartupCommand.IsLegacyEntry(command))
                key.DeleteValue("PetTracker", false);
        }
        catch (Exception error)when (error is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
        // Best effort retirement. Tracking must still open if registry access is denied.
        }
    }
}
