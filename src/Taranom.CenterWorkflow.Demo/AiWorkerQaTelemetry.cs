namespace Taranom.CenterWorkflow.Demo;

// TRN-TECH-152: process-local, fixed-dimensional, non-identifying synthetic
// counters only. This is NOT a durable audit, clinical monitoring, or evidence
// of human authorization. No request/tenant/model/token/exception string.
internal enum AiWorkerQaResult
{
    Success, ProcessRejected, InvalidReply, DeadlineExceeded,
    RequestCancelled, Unavailable
}

internal sealed class AiWorkerQaFailure(AiWorkerQaResult result)
    : InvalidOperationException("AI_WORKER_TEST_EXECUTION_REJECTED")
{
    internal AiWorkerQaResult Result { get; } = result;
}

internal readonly record struct AiWorkerQaSnapshot(
    long Started, long Success, long ProcessRejected, long InvalidReply,
    long DeadlineExceeded, long RequestCancelled, long Unavailable,
    long CapacityDenied, long InFlight, long PeakInFlight);

internal sealed class AiWorkerQaCounters
{
    private long started;
    private long success;
    private long processRejected;
    private long invalidReply;
    private long deadlineExceeded;
    private long requestCancelled;
    private long unavailable;
    private long capacityDenied;
    private long inFlight;
    private long peakInFlight;

    internal void DenyCapacity() => Interlocked.Increment(ref capacityDenied);
    internal void Start()
    {
        Interlocked.Increment(ref started);
        var now = Interlocked.Increment(ref inFlight);
        while (true)
        {
            var old = Interlocked.Read(ref peakInFlight);
            if (now <= old ||
                Interlocked.CompareExchange(ref peakInFlight,now,old) == old)
                break;
        }
    }
    internal void Complete(AiWorkerQaResult result)
    {
        switch (result)
        {
            case AiWorkerQaResult.Success:
                Interlocked.Increment(ref success); break;
            case AiWorkerQaResult.ProcessRejected:
                Interlocked.Increment(ref processRejected); break;
            case AiWorkerQaResult.InvalidReply:
                Interlocked.Increment(ref invalidReply); break;
            case AiWorkerQaResult.DeadlineExceeded:
                Interlocked.Increment(ref deadlineExceeded); break;
            case AiWorkerQaResult.RequestCancelled:
                Interlocked.Increment(ref requestCancelled); break;
            default:
                Interlocked.Increment(ref unavailable); break;
        }
        Interlocked.Decrement(ref inFlight);
    }
    internal AiWorkerQaSnapshot Snapshot() => new(
        Interlocked.Read(ref started), Interlocked.Read(ref success),
        Interlocked.Read(ref processRejected), Interlocked.Read(ref invalidReply),
        Interlocked.Read(ref deadlineExceeded), Interlocked.Read(ref requestCancelled),
        Interlocked.Read(ref unavailable), Interlocked.Read(ref capacityDenied),
        Interlocked.Read(ref inFlight), Interlocked.Read(ref peakInFlight));
}
