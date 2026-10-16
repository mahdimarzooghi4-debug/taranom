using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Taranom.CenterWorkflow.Demo;

// TRN-TECH-150: adversarial parse proofing of the real .NET production-language
// validator, but in a non-deployable, synthetic-only executable probe.
if (Environment.GetEnvironmentVariable("TARANOM_CENTER_DEMO_TEST_ONLY") !=
    "CI_EPHEMERAL_ONLY")
    throw new InvalidOperationException("AI_WORKER_PROBE_TEST_ONLY");
const key = Convert.FromHexString(string.Concat(Enumerable.Repeat("8F",32)));
const request = "test:probe-a";
const tenant = "test:team-a";
const nonce = new string('a',32);
string Mac(params string[] parts) =>
    Convert.ToHexString(HMACSHA256.HashData(key,
        Encoding.UTF8.GetBytes(string.Join("\n",parts))));
JsonObject Sample()
{
    var counts = new JsonObject();
    foreach(var name in AiWorkerReplyVerifier.Fields) counts[name] = 0;
    return new JsonObject
    {
        ["contract"] = AiWorkerReplyVerifier.Contract,
        ["command"] = AiWorkerReplyVerifier.Command,
        ["requestRef"] = request,
        ["tenantRef"] = tenant,
        ["nonce"] = nonce,
        ["status"] = AiWorkerReplyVerifier.Status,
        ["counts"] = counts,
        ["syntheticOnly"] = true,
        ["adminAuthenticated"] = false,
        ["trainingAllowed"] = false,
        ["productionAllowed"] = false,
        ["mac"] = Mac(AiWorkerReplyVerifier.Contract,AiWorkerReplyVerifier.Command,
            request,tenant,nonce,AiWorkerReplyVerifier.Status,
            "true","false","false","false","0","0","0","0","0")
    };
}
byte[] Encode(JsonObject o) => Encoding.UTF8.GetBytes(o.ToJsonString());
void Accept(byte[] bytes)
{
    var counts=AiWorkerReplyVerifier.Verify(bytes,key,request,tenant,nonce);
    if(counts.Count != 5 || counts.Values.Any(v=>v!=0))
        throw new Exception("AI_WORKER_PROBE_FALSE_POSITIVE");
}
void Reject(byte[] bytes)
{
    try { AiWorkerReplyVerifier.Verify(bytes,key,request,tenant,nonce); }
    catch(InvalidOperationException ex) when(ex.Message == "WORKER_REPLY_UNVERIFIABLE")
    { return; }
    throw new Exception("AI_WORKER_PROBE_UNSAFE_ACCEPT");
}
void Mutate(Action<JsonObject> change)
{
    var obj=Sample();
    change(obj);
    Reject(Encode(obj));
}
Accept(Encode(Sample()));
Mutate(v=>v["mac"]="0" + v["mac"]!.GetValue<string>()[1..]);
Mutate(v=>v["contract"]="TRN-TECH-142");
Mutate(v=>v["command"]="AUTHORIZE_PRODUCTION");
Mutate(v=>v["requestRef"]="test:other");
Mutate(v=>v["tenantRef"]="test:other");
Mutate(v=>v["nonce"]="b" + nonce[1..]);
Mutate(v=>v["status"]="MODEL_ACTIVATED");
Mutate(v=>v["syntheticOnly"]=false);
Mutate(v=>v["adminAuthenticated"]=true);
Mutate(v=>v["trainingAllowed"]=true);
Mutate(v=>v["productionAllowed"]=true);
Mutate(v=>v["counts"]!["datasets"]=4097);
Mutate(v=>v["counts"]!["benchmarks"]=-1);
Mutate(v=>v["counts"]!["trainingRuns"]="0");
Mutate(v=>v["counts"]!["evaluations"]=0.5);
Mutate(v=>v["counts"]!["unapproved"]=1);
Mutate(v=>v["counts"]!.AsObject().Remove("datasets"));
Mutate(v=>v["unknownAuthority"]=true);
Mutate(v=>v.Remove("mac"));
Mutate(v=>v.Remove("counts"));
Mutate(v=>v["mac"]=null);
Mutate(v=>v["mac"]=false);
Reject(Encoding.UTF8.GetBytes("{}"));
Reject(Encoding.UTF8.GetBytes("{invalid"));
Reject(Encoding.UTF8.GetBytes("[]"));
Reject(Encoding.UTF8.GetBytes(Sample().ToJsonString()[..^1] +
    ",\"nonce\":\""+nonce+"\"}"));
Reject(Encoding.UTF8.GetBytes(Sample().ToJsonString()[..^1] +
    ",\"counts\":{}}"));
Reject([0xff,0xfe,0x7b,0x7d]);
Reject(new byte[2049]);
Reject(Encode(Sample()).AsMemory(0,1).ToArray());
// The same envelope is bound to this exact request, tenant and nonce.
try { AiWorkerReplyVerifier.Verify(Encode(Sample()),key,request,"test:other",nonce); }
catch(InvalidOperationException) { goto tenantPassed; }
throw new Exception("AI_WORKER_PROBE_TENANT_REPLAY");
tenantPassed:
try { AiWorkerReplyVerifier.Verify(Encode(Sample()),key,request,tenant,"b"+nonce[1..]); }
catch(InvalidOperationException) { goto noncePassed; }
throw new Exception("AI_WORKER_PROBE_NONCE_REPLAY");
noncePassed:
Console.WriteLine("PASS: 30 adversarial worker response cases + exact HMAC/correlation/denied-authority admission");
