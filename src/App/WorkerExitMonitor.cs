using System.Diagnostics;

namespace PetTracker;

public sealed class WorkerExitMonitor : IDisposable
{
    readonly Process worker;
    WorkerExitMonitor(Process worker)
    {
        this.worker = worker;
    }
    public static WorkerExitMonitor Open(int processId, string expectedPath)
    {
        var process = Process.GetProcessById(processId);
        try
        {
            // GetProcessById does not retain a handle. Open it BEFORE sending exit,
            // otherwise Windows can discard the exit code before our next UI poll.
            _ = process.Handle;
            if (!string.Equals(process.MainModule?.FileName, Path.GetFullPath(expectedPath), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Capture process path does not match this installation.");
            return new(process);
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }
    public bool ExitedCleanly
    {
        get
        {
            try
            {
                return worker.HasExited && worker.ExitCode == 0;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }
    public void Dispose() => worker.Dispose();
}
