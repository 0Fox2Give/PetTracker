namespace PetTracker;

// Called under the engine gate, also used by manual capture restarts. Time is
// monotonic so a system clock adjustment cannot stall or accelerate retries.
public sealed class CaptureRecovery(Func<bool> running, Action invalidate, Action restart, Func<long>? clock = null)
{
    readonly Func<long> now = clock ?? (() => Environment.TickCount64);
    bool wasRunning;
    long? nextAttempt;
    int failures;
    public void Tick()
    {
        if (running())
        {
            wasRunning = true;
            nextAttempt = null;
            failures = 0;
            return;
        }
        if (wasRunning)
        {
            invalidate();
            wasRunning = false;
        }
        long time = now();
        if (nextAttempt == null)
        {
            nextAttempt = time + 5000;
            return;
        }
        if (time < nextAttempt)
            return;
        // Reserve the next attempt before calling transport code: exceptions must
        // not turn the one-second maintenance loop into a tight restart loop.
        failures = Math.Min(failures + 1, 4);
        nextAttempt = time + Math.Min(60000, 5000L * (1L << failures));
        restart();
    }
}
