using Taranom.CenterWorkflow.Demo;

// TRN-TECH-154: ephemeral filesystem tests for bounded append/rehydration,
// concurrent operations and tamper-denial. No patient/actor/tenant data.
if(Environment.GetEnvironmentVariable("TARANOM_CENTER_DEMO_TEST_ONLY")!=
    "CI_EPHEMERAL_ONLY" ||
   Environment.GetEnvironmentVariable("TARANOM_AI_WORKER_EVIDENCE_TEST_ONLY")!=
    "CI_EPHEMERAL_ONLY")
    throw new InvalidOperationException("EVIDENCE_PROBE_TEST_ONLY");
var dir=Path.Combine("/tmp","taranom-worker-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(dir);
var file=Path.Combine(dir,"worker-evidence.jsonl");
try
{
    var old=new string('a',32);
    var epoch2=new string('b',32);
    var ledger=new AiWorkerQaEvidenceLedger(file);
    var initial=ledger.Verify();
    if(initial.Entries!=0 || initial.BootCount!=0)
        throw new InvalidOperationException("EVIDENCE_PROBE_NONEMPTY");
    ledger.Append(old,AiWorkerQaEvidenceKind.Boot);
    Parallel.For(0,100,i=>ledger.Append(old,i%2==0
        ? AiWorkerQaEvidenceKind.Success
        : AiWorkerQaEvidenceKind.ProcessRejected));
    var before=ledger.Verify();
    if(before.Entries!=101 || before.BootCount!=1 ||
       before.LastEpoch!=old || before.LastDigest.Length!=64)
        throw new InvalidOperationException("EVIDENCE_PROBE_CHAIN_INCONSISTENT");
    var reloaded=new AiWorkerQaEvidenceLedger(file);
    if(reloaded.Verify()!=before)
        throw new InvalidOperationException("EVIDENCE_PROBE_RELOAD_LOST");
    reloaded.Append(epoch2,AiWorkerQaEvidenceKind.Boot);
    reloaded.Append(epoch2,AiWorkerQaEvidenceKind.Success);
    var after=reloaded.Verify();
    if(after.Entries!=103 || after.BootCount!=2 || after.LastEpoch!=epoch2)
        throw new InvalidOperationException("EVIDENCE_PROBE_RESTART_LOST");
    // A new epoch cannot issue a non-boot event before explicit Boot.
    try { reloaded.Append(old,AiWorkerQaEvidenceKind.Success);
          throw new Exception("EVIDENCE_PROBE_ACCEPTED_STALE_EPOCH"); }
    catch(InvalidOperationException) { }

    var good=File.ReadAllBytes(file);
    var changed=File.ReadAllText(file).Replace("\"Boot\"","\"Booz\"",StringComparison.Ordinal);
    File.WriteAllText(file,changed);
    try { reloaded.Verify(); throw new Exception("EVIDENCE_PROBE_TAMPER_ACCEPTED"); }
    catch(InvalidOperationException) { }
    try { reloaded.Append(epoch2,AiWorkerQaEvidenceKind.Success);
          throw new Exception("EVIDENCE_PROBE_TAMPER_APPEND_ACCEPTED"); }
    catch(InvalidOperationException) { }
    File.WriteAllBytes(file,good);
    if(reloaded.Verify()!=after)
        throw new InvalidOperationException("EVIDENCE_PROBE_RECOVERY_FAILED");
    File.AppendAllText(file,"CORRUPT_TAIL");
    try { reloaded.Verify(); throw new Exception("EVIDENCE_PROBE_PARTIAL_ACCEPTED"); }
    catch(InvalidOperationException) { }
    File.WriteAllBytes(file,good);
    if(reloaded.Verify()!=after)
        throw new InvalidOperationException("EVIDENCE_PROBE_TAIL_RECOVERY");
    try
    {
        _=new AiWorkerQaEvidenceLedger(Path.Combine("/tmp","worker-evidence.jsonl"));
        throw new Exception("EVIDENCE_PROBE_BAD_PATH_ACCEPTED");
    }
    catch(InvalidOperationException) { }
    var text=File.ReadAllText(file);
    foreach(var forbidden in new[]{"requestRef","tenantRef","Bearer",
        "userId","patient","diagnosis","token","modelRef","stderr",
        "TOP_SECRET_TEST_SENTINEL"})
        if(text.Contains(forbidden,StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("EVIDENCE_PROBE_PRIVACY_LEAK");
    Console.WriteLine("PASS: concurrent hash-linked synthetic evidence, reload, restart, tamper and partial-tail denial");
}
finally { Directory.Delete(dir,true); }
