namespace PetTracker;

public sealed record CapturedPayload(int SourcePort, int DestinationPort, byte[] Data, string? DeviceName);
public interface IPacketCaptureService : IDisposable
{
    bool IsRunning
    {
        get;
    }
    long TcpGaps
    {
        get;
    }
    event EventHandler<CapturedPayload>? PayloadCaptured;
    void Start(); void Stop(); (long Received, long Dropped) GetCaptureStatistics();
}
