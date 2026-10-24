using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Taranom.CenterWorkflow.Demo;

// TRN-TECH-151: real Python child failures injected through direct probe,
// NEVER from HTTP. This test is not a Production worker adapter.
if(Environment.GetEnvironmentVariable("TARANOM_CENTER_DEMO_TEST_ONLY") !=
        "CI_EPHEMERAL_ONLY")
    throw new InvalidOperationException("FAULT_PROBE_TEST_ONLY");
var root=Directory.GetCurrentDirectory();
var script=Path.GetFullPath("tests/ai/fault_worker_fixture.py");
if(!File.Exists(script) || !script.StartsWith(root+Path.DirectorySeparatorChar,
       StringComparison.Ordinal))
    throw new InvalidOperationException("FAULT_PROBE_FIXTURE_UNAVAILABLE");
var key=RandomNumberGenerator.GetBytes(32);
const string request="test:probe-fault-1";
const string tenant="test:tenant-fault";
var nonce=Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
var frame=JsonSerializer.Serialize(new
{
    contract=AiWorkerReplyVerifier.Contract,
    command=AiWorkerReplyVerifier.Command,
    requestRef=request,tenantRef=tenant,nonce,
    mac=AiWorkerReplyVerifier.Mac(key,AiWorkerReplyVerifier.Contract,
         AiWorkerReplyVerifier.Command,request,tenant,nonce)
});
ProcessStartInfo Command(string mode)
{
    var info=new ProcessStartInfo("/usr/bin/python3")
    {
        UseShellExecute=false,RedirectStandardInput=true,
        RedirectStandardOutput=true,RedirectStandardError=true,
        WorkingDirectory=root,CreateNoWindow=true
    };
    info.ArgumentList.Add("-B");
    info.ArgumentList.Add(script);
    info.ArgumentList.Add(mode);
    // Only one ephemeral probe key. No owner DB credentials or token.
    info.Environment.Clear();
    info.Environment["TARANOM_FAULT_PROBE_KEY"]=Convert.ToHexString(key);
    return info;
}
async Task Run(string mode,bool expectedSuccess,TimeSpan? deadline=null)
{
    try
    {
        var result=await AiWorkerQaHttp.RunProcessAsync(
            Command(mode),frame,key,request,tenant,nonce,
            deadline ?? TimeSpan.FromSeconds(3));
        if(!expectedSuccess || result.Count!=5 || result.Values.Any(n=>n!=0))
            throw new Exception("FAULT_PROBE_UNEXPECTED_ACCEPT_"+mode);
    }
    catch(InvalidOperationException ex)
    {
        if(expectedSuccess)
            throw new Exception("FAULT_PROBE_FALSE_REJECTION_"+mode,ex);
        // Security boundary must never include worker stdout/stderr or
        // simulated credentials in a propagated error.
        if(ex.ToString().Contains("TOP_SECRET_TEST_SENTINEL",
              StringComparison.Ordinal))
            throw new Exception("FAULT_PROBE_SECRET_LEAK_"+mode);
    }
}
await Run("ok",true);
await Run("fragmented",true);
foreach(var mode in new[]{
    "die-early","silence","bad-json","invalid-utf8",
    "overflow-stdout","stderr-leak","overflow-stderr",
    "bad-proof","wrong-tenant","trailing-garbage",
    "valid-but-nonzero"})
    await Run(mode,false);
await Run("sleep-before-output",false,TimeSpan.FromMilliseconds(250));
await Run("hang-after-output",false,TimeSpan.FromMilliseconds(250));
// Demonstrate that a worker failure does not poison the next read.
await Run("ok",true);
Console.WriteLine("PASS: 16 subprocess fault/recovery probes, no unsafe success or secret propagation");
