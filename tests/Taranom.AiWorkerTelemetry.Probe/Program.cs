using System.Text.Json;
using Taranom.CenterWorkflow.Demo;

// TRN-TECH-152: independent nondeployable thread-safe aggregation tests.
// Only fixed event kinds; never request/tenant/secret/free-text dimensions.
if (Environment.GetEnvironmentVariable("TARANOM_CENTER_DEMO_TEST_ONLY") !=
    "CI_EPHEMERAL_ONLY")
    throw new InvalidOperationException("WORKER_TELEMETRY_TEST_ONLY");

var counter = new AiWorkerQaCounters();
var empty=counter.Snapshot();
if(empty.Started != 0 || empty.InFlight != 0 ||
    empty.CapacityDenied != 0 || empty.PeakInFlight != 0)
    throw new InvalidOperationException("NON_EMPTY_TEST_COUNTERS");
for(var i=0;i<4;i++) counter.Start();
var initial=counter.Snapshot();
if(initial.Started != 4 || initial.InFlight != 4 || initial.PeakInFlight != 4)
    throw new InvalidOperationException("PEAK_TEST_FAILED");
counter.Complete(AiWorkerQaResult.Success);
counter.Complete(AiWorkerQaResult.ProcessRejected);
counter.Complete(AiWorkerQaResult.InvalidReply);
counter.Complete(AiWorkerQaResult.DeadlineExceeded);
counter.DenyCapacity();
counter.DenyCapacity();
var mid=counter.Snapshot();
if(mid.Started!=4 || mid.InFlight!=0 || mid.PeakInFlight!=4 ||
    mid.Success!=1 || mid.ProcessRejected!=1 || mid.InvalidReply!=1 ||
    mid.DeadlineExceeded!=1 || mid.CapacityDenied!=2)
    throw new InvalidOperationException("CLASSIFICATION_FAILED");

// Concurrency safety under genuine .NET threads, no mock counters.
Parallel.For(0,400,i =>
{
    counter.Start();
    var kind=(i%4) switch
    {
        0 => AiWorkerQaResult.Success,
        1 => AiWorkerQaResult.RequestCancelled,
        2 => AiWorkerQaResult.Unavailable,
        _ => AiWorkerQaResult.ProcessRejected
    };
    counter.Complete(kind);
    if (i%5==0) counter.DenyCapacity();
});
var x=counter.Snapshot();
if(x.Started!=404 || x.InFlight!=0 || x.CapacityDenied!=82 ||
    x.Success!=101 || x.RequestCancelled!=100 || x.Unavailable!=100 ||
    x.ProcessRejected!=101 || x.InvalidReply!=1 || x.DeadlineExceeded!=1 ||
    x.PeakInFlight<4)
    throw new InvalidOperationException("COUNTER_CONCURRENCY_LOST");
// Serialization is fixed-dimensional, contains no arbitrary actor,
// request reference, source contents, signed frames or diagnosis.
var serialized=JsonSerializer.Serialize(x);
foreach(var forbidden in new[]{"requestRef","tenantRef","auth","token","secret",
    "patient","modelRef","clinical","mac","exception"})
{
    if(serialized.Contains(forbidden,StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("HIGH_CARDINALITY_TELEMETRY");
}
Console.WriteLine("PASS: fixed-label thread-safe synthetic worker fault telemetry; no identity or secret fields");
