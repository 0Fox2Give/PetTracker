using System.Security.Cryptography;
using System.Text;
namespace PetTracker;

// This gate deliberately excludes the data path: test/portable/installed copies
// must not create independent HUDs for the same Windows user and session.
public sealed class AppInstanceGate : IDisposable
{
    readonly Mutex mutex;
    bool disposed;
    public bool IsPrimary
    {
        get;
    }
    public static string Name(string user) => "Local\\PetTracker-Desktop-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(user)))[..24];
    public AppInstanceGate(string? user = null)
    {
        mutex = new Mutex(false, Name(user ?? Environment.UserDomainName + "\\" + Environment.UserName));
        try
        {
            IsPrimary = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            IsPrimary = true;
        }
    }
    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        if (IsPrimary)
            mutex.ReleaseMutex();
        mutex.Dispose();
    }
}
