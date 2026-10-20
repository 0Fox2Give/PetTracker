namespace PetTracker;
public static class MaintenanceLoop
{
    public static async Task Run(Action tick, Action<Exception?> report, CancellationToken stop, TimeSpan? interval = null)
    {
        try
        {
            while (true)
            {
                await Task.Delay(interval ?? TimeSpan.FromSeconds(1), stop);
                try
                {
                    tick();
                    report(null);
                }
                catch (OperationCanceledException)when (stop.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception error)
                {
                    report(error);
                }
            }
        }
        catch (OperationCanceledException)when (stop.IsCancellationRequested)
        {
        }
    }
}
