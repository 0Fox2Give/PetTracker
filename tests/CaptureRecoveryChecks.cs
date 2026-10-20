using PetTracker;

static class CaptureRecoveryChecks
{
    public static void Run(Action<bool, string> check)
    {
        long time = 0;
        bool running = false;
        int attempts = 0, invalidations = 0;
        var recovery = new CaptureRecovery(() => running, () => invalidations++, () => attempts++, () => time);
        recovery.Tick();
        time = 4999;
        recovery.Tick();
        check(attempts == 0 && invalidations == 0, "Startup capture failure waits five seconds without invalidating a nonexistent session");
        foreach (long due in new long[] { 5000, 15000, 35000, 75000, 135000, 195000 })
        {
            time = due - 1;
            int before = attempts;
            recovery.Tick();
            check(attempts == before, "Capture retry does not run before its deadline");
            time = due;
            recovery.Tick();
            check(attempts == before + 1, "Capture failures retry with a delay capped at sixty seconds");
        }
        running = true;
        recovery.Tick();
        time += 100000;
        recovery.Tick();
        check(attempts == 6, "A healthy but quiet capture is not restarted merely because time passed");
        running = false;
        recovery.Tick();
        recovery.Tick();
        check(invalidations == 1, "A failed reader invalidates its previous session exactly once");
        time += 5000;
        recovery.Tick();
        check(attempts == 7, "Recovery after a healthy session starts again with a five-second delay");
        time = 0;
        int throws = 0;
        var throwing = new CaptureRecovery(() => false, () => { }, () =>
        {
            throws++;
            throw new IOException("Adapter unavailable");
        }, () => time);
        throwing.Tick();
        time = 5000;
        try
        {
            throwing.Tick();
        }
        catch (IOException) { }
        time = 5001;
        throwing.Tick();
        check(throws == 1, "A throwing capture restart still honors retry backoff");
    }
}
