using System.Data;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;

namespace Taranom.ClinicalEvidenceJournal.Postgres;

internal enum MedicalSourceReviewOutcome { Recorded, Replayed, ConflictingReplay, StaleRevision, ScopeDenied, Invalid }
internal sealed record MedicalSourceReviewSource(string SourceRef,string PackageSha256);
internal sealed record MedicalSourceReviewPackageStream(string SourceRef,Stream Content);

internal static class MedicalSourcePackageFingerprint
{
    internal static async Task<string> ComputeSha256Async(Stream content,long maximumBytes,CancellationToken ct=default)
    {
        if(content is null||!content.CanRead)throw new ArgumentException("A readable package stream is required.",nameof(content));
        if(maximumBytes<=0)throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer=new byte[64*1024];
        long total=0;
        while(true)
        {
            var read=await content.ReadAsync(buffer.AsMemory(),ct);
            if(read==0)break;
            if(read>maximumBytes-total)throw new InvalidOperationException("MEDICAL_SOURCE_PACKAGE_SIZE_LIMIT");
            hash.AppendData(buffer,0,read);
            total+=read;
        }
        if(total==0)throw new InvalidOperationException("MEDICAL_SOURCE_PACKAGE_EMPTY");
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
internal sealed record MedicalSourceReviewSubmission(Guid RequestId,long ExpectedRevision,string SourceRef,
    string PackageSha256,string Lane,string Recommendation,string ReviewerRefClaimed,string EvidenceRefClaimed);
internal sealed record MedicalSourceReviewCommand(Guid RequestId,long ExpectedRevision,string SourceRef,
    string PackageSha256,string Lane,string Recommendation,string ReviewerRefClaimed,string EvidenceRefClaimed,string EvidencePackageSha256);
internal sealed record MedicalSourceReviewEvent(long Revision,MedicalSourceReviewCommand Command,string PreviousSha256,string EventSha256);
internal sealed record MedicalSourceReviewSnapshot(long Revision,string HeadSha256,IReadOnlyList<MedicalSourceReviewEvent> Events,
    bool ReviewerIdentityAuthenticated,bool EvidenceCustodyVerified,bool RightsVerified,bool ScientificallyReviewed,
    bool TaxonomyApproved,bool TrainingAllowed,bool ClinicalUseAllowed,bool ProductionAllowed);
internal readonly record struct MedicalSourceReviewAppendResult(MedicalSourceReviewOutcome Outcome,long CurrentRevision);

// Synthetic-only durable journal. Caller identity/evidence references remain claims.
internal sealed class PgMedicalSourceReviewJournal(NpgsqlDataSource provisioningDataSource, NpgsqlDataSource writerDataSource, NpgsqlDataSource readerDataSource)
{
    private static readonly HashSet<string> Lanes=["RIGHTS","SCIENCE","TAXONOMY"];
    private static readonly HashSet<string> Recommendations=["REQUEST_EVIDENCE","RECOMMEND_ACCEPT","RECOMMEND_REJECT"];

    internal static async Task InitializeTestSchemaAsync(NpgsqlDataSource owner,CancellationToken ct=default)
    {
        const string ddl="""
        CREATE TABLE IF NOT EXISTS internal_test_medical_review_head(
          catalog_sha256 text PRIMARY KEY CHECK(catalog_sha256 ~ '^[0-9a-f]{64}$'),
          taxonomy_sha256 text NOT NULL CHECK(taxonomy_sha256 ~ '^[0-9a-f]{64}$'),
          current_revision bigint NOT NULL DEFAULT 0 CHECK(current_revision>=0),
          head_sha256 text NOT NULL CHECK(head_sha256 ~ '^[0-9a-f]{64}$'));
        CREATE TABLE IF NOT EXISTS internal_test_medical_review_source(
          catalog_sha256 text NOT NULL REFERENCES internal_test_medical_review_head(catalog_sha256),
          source_ref text NOT NULL CHECK(source_ref ~ '^test:[A-Za-z0-9_:-]{1,120}$'),
          package_sha256 text NOT NULL CHECK(package_sha256 ~ '^[0-9a-f]{64}$'),
          PRIMARY KEY(catalog_sha256,source_ref),UNIQUE(catalog_sha256,source_ref,package_sha256));
        CREATE TABLE IF NOT EXISTS internal_test_medical_review_event(
          catalog_sha256 text NOT NULL,revision bigint NOT NULL CHECK(revision>0),request_id uuid NOT NULL,
          source_ref text NOT NULL,package_sha256 text NOT NULL,command_json text NOT NULL,
          previous_sha256 text NOT NULL CHECK(previous_sha256 ~ '^[0-9a-f]{64}$'),
          event_sha256 text NOT NULL CHECK(event_sha256 ~ '^[0-9a-f]{64}$'),
          PRIMARY KEY(catalog_sha256,revision),UNIQUE(catalog_sha256,request_id),
          FOREIGN KEY(catalog_sha256,source_ref,package_sha256)
            REFERENCES internal_test_medical_review_source(catalog_sha256,source_ref,package_sha256));
        CREATE TABLE IF NOT EXISTS internal_test_medical_review_audit(
          catalog_sha256 text NOT NULL,revision bigint NOT NULL,request_id uuid NOT NULL,event_sha256 text NOT NULL,
          recorded_at timestamptz NOT NULL DEFAULT now(),PRIMARY KEY(catalog_sha256,revision),
          UNIQUE(catalog_sha256,request_id),
          FOREIGN KEY(catalog_sha256,revision)
            REFERENCES internal_test_medical_review_event(catalog_sha256,revision));
        CREATE OR REPLACE FUNCTION internal_test_medical_review_guard()
        RETURNS trigger LANGUAGE plpgsql AS $fn$
        BEGIN
          IF TG_OP='INSERT' AND TG_TABLE_NAME='internal_test_medical_review_audit'
             AND NEW.catalog_sha256 IN (SELECT catalog_sha256 FROM internal_test_medical_review_source
               WHERE source_ref LIKE 'test:abort-medical-audit-%') THEN
            RAISE EXCEPTION 'TEST_MEDICAL_REVIEW_AUDIT_UNAVAILABLE' USING ERRCODE='P0001';
          END IF;
          IF TG_OP='INSERT' THEN RETURN NEW; END IF;
          RAISE EXCEPTION 'TEST_MEDICAL_REVIEW_APPEND_ONLY' USING ERRCODE='P0001';
        END; $fn$;
        DROP TRIGGER IF EXISTS medical_review_source_guard_tr ON internal_test_medical_review_source;
        CREATE TRIGGER medical_review_source_guard_tr BEFORE UPDATE OR DELETE ON internal_test_medical_review_source
          FOR EACH ROW EXECUTE FUNCTION internal_test_medical_review_guard();
        DROP TRIGGER IF EXISTS medical_review_event_guard_tr ON internal_test_medical_review_event;
        CREATE TRIGGER medical_review_event_guard_tr BEFORE UPDATE OR DELETE ON internal_test_medical_review_event
          FOR EACH ROW EXECUTE FUNCTION internal_test_medical_review_guard();
        DROP TRIGGER IF EXISTS medical_review_audit_guard_tr ON internal_test_medical_review_audit;
        CREATE TRIGGER medical_review_audit_guard_tr BEFORE INSERT OR UPDATE OR DELETE ON internal_test_medical_review_audit
          FOR EACH ROW EXECUTE FUNCTION internal_test_medical_review_guard();
        """;
        await using var cn=await owner.OpenConnectionAsync(ct);
        await using var cmd=new NpgsqlCommand(ddl,cn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    internal async Task OpenAndStorePackageStreamsAsync(string catalog,string taxonomy,
        IReadOnlyCollection<MedicalSourceReviewPackageStream> packages,MedicalSourcePackageBlobStore blobStore,
        long maximumBytesPerPackage,CancellationToken ct=default)
    {
        if(!Sha(catalog)||!Sha(taxonomy)||packages is null||packages.Count is <1 or >32||
           blobStore is null||maximumBytesPerPackage<=0||
           packages.Any(x=>x is null||!TestRef(x.SourceRef)||x.Content is null||!x.Content.CanRead)||
           packages.Select(x=>x.SourceRef).Distinct(StringComparer.Ordinal).Count()!=packages.Count)
            throw new ArgumentException("Invalid synthetic medical review package scope.");
        var sources=new List<MedicalSourceReviewSource>(packages.Count);
        foreach(var package in packages)
        {
            var stored=await blobStore.StoreAsync(package.Content,maximumBytesPerPackage,ct);
            sources.Add(new(package.SourceRef,stored.Sha256));
        }
        await OpenHashedSourcesAsync(catalog,taxonomy,sources,ct);
    }

    internal async Task<MedicalSourceReviewAppendResult> AppendWithStoredEvidenceAsync(string catalog,string taxonomy,
        MedicalSourceReviewSubmission input,Stream evidencePackage,MedicalSourcePackageBlobStore blobStore,
        long maximumBytes,CancellationToken ct=default)
    {
        if(blobStore is null)throw new ArgumentNullException(nameof(blobStore));
        if(maximumBytes<=0)throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        if(!Sha(catalog)||!Sha(taxonomy)||!Valid(input))return new(MedicalSourceReviewOutcome.Invalid,0);
        if(!await blobStore.VerifyAsync(input.PackageSha256,maximumBytes,ct))
            throw new InvalidOperationException("MEDICAL_SOURCE_PACKAGE_BLOB_MISSING");
        var evidence=await blobStore.StoreAsync(evidencePackage,maximumBytes,ct);
        if(!await blobStore.VerifyAsync(evidence.Sha256,maximumBytes,ct))
            throw new InvalidOperationException("MEDICAL_REVIEW_EVIDENCE_BLOB_MISSING");
        var command=new MedicalSourceReviewCommand(input.RequestId,input.ExpectedRevision,input.SourceRef,
            input.PackageSha256,input.Lane,input.Recommendation,input.ReviewerRefClaimed,input.EvidenceRefClaimed,evidence.Sha256);
        return await AppendAsync(catalog,taxonomy,command,ct);
    }

    internal async Task<MedicalSourceReviewAppendResult> AppendWithStoredPackageAsync(string catalog,string taxonomy,
        MedicalSourceReviewCommand input,MedicalSourcePackageBlobStore blobStore,long maximumBytes,CancellationToken ct=default)
    {
        if(blobStore is null)throw new ArgumentNullException(nameof(blobStore));
        if(maximumBytes<=0)throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        if(!Sha(catalog)||!Sha(taxonomy)||!Valid(input))return new(MedicalSourceReviewOutcome.Invalid,0);
        if(!await blobStore.VerifyAsync(input.PackageSha256,maximumBytes,ct))
            throw new InvalidOperationException("MEDICAL_SOURCE_PACKAGE_BLOB_MISSING");
        return await AppendAsync(catalog,taxonomy,input,ct);
    }

    internal async Task OpenFromPackageStreamsAsync(string catalog,string taxonomy,
        IReadOnlyCollection<MedicalSourceReviewPackageStream> packages,long maximumBytesPerPackage,CancellationToken ct=default)
    {
        if(!Sha(catalog)||!Sha(taxonomy)||packages is null||packages.Count is <1 or >32||maximumBytesPerPackage<=0)
            throw new ArgumentException("Invalid synthetic medical review package scope.");
        var refs=new HashSet<string>(StringComparer.Ordinal);
        var sources=new List<MedicalSourceReviewSource>(packages.Count);
        foreach(var package in packages)
        {
            if(package is null||!TestRef(package.SourceRef)||package.Content is null||!refs.Add(package.SourceRef))
                throw new ArgumentException("Invalid synthetic medical review package.");
            var digest=await MedicalSourcePackageFingerprint.ComputeSha256Async(package.Content,maximumBytesPerPackage,ct);
            sources.Add(new(package.SourceRef,digest));
        }
        await OpenHashedSourcesAsync(catalog,taxonomy,sources,ct);
    }

    private async Task OpenHashedSourcesAsync(string catalog,string taxonomy,IReadOnlyCollection<MedicalSourceReviewSource> sources,CancellationToken ct=default)
    {
        if(!Sha(catalog)||!Sha(taxonomy)||sources is null||sources.Count is <1 or >32||
           sources.Any(x=>!TestRef(x.SourceRef)||!Sha(x.PackageSha256))||
           sources.Select(x=>x.SourceRef).Distinct(StringComparer.Ordinal).Count()!=sources.Count)
            throw new ArgumentException("Invalid synthetic medical review catalog.");
        var genesis=Digest($"genesis\n{catalog}\n{taxonomy}");
        await using var cn=await provisioningDataSource.OpenConnectionAsync(ct);
        await using var tx=await cn.BeginTransactionAsync(ct);
        await using(var insert=new NpgsqlCommand("""
          INSERT INTO internal_test_medical_review_head(catalog_sha256,taxonomy_sha256,head_sha256)
          VALUES(@c,@t,@g) ON CONFLICT DO NOTHING
          """,cn,tx))
        {
            insert.Parameters.AddWithValue("c",catalog);insert.Parameters.AddWithValue("t",taxonomy);
            insert.Parameters.AddWithValue("g",genesis);await insert.ExecuteNonQueryAsync(ct);
        }
        long revision;string storedTaxonomy;string storedHead;
        await using(var check=new NpgsqlCommand("""
          SELECT current_revision,taxonomy_sha256,head_sha256 FROM internal_test_medical_review_head
          WHERE catalog_sha256=@c FOR UPDATE
          """,cn,tx))
        {
            check.Parameters.AddWithValue("c",catalog);
            await using var reader=await check.ExecuteReaderAsync(ct);
            if(!await reader.ReadAsync(ct))throw new InvalidOperationException("MEDICAL_REVIEW_HEAD_MISSING");
            revision=reader.GetInt64(0);storedTaxonomy=reader.GetString(1);storedHead=reader.GetString(2);
        }
        if(storedTaxonomy!=taxonomy||(revision==0&&storedHead!=genesis)||(revision>0&&storedHead==genesis))
            throw new InvalidOperationException("MEDICAL_REVIEW_CATALOG_SCOPE_CONFLICT");
        var existingSources=new List<MedicalSourceReviewSource>();
        await using(var existingQuery=new NpgsqlCommand("""
          SELECT source_ref,package_sha256 FROM internal_test_medical_review_source
          WHERE catalog_sha256=@c ORDER BY source_ref
          """,cn,tx))
        {
            existingQuery.Parameters.AddWithValue("c",catalog);
            await using var existingReader=await existingQuery.ExecuteReaderAsync(ct);
            while(await existingReader.ReadAsync(ct))
                existingSources.Add(new(existingReader.GetString(0),existingReader.GetString(1)));
        }
        var requestedSources=sources.OrderBy(x=>x.SourceRef,StringComparer.Ordinal).ToArray();
        if((revision>0&&existingSources.Count==0)||(existingSources.Count>0&&!existingSources.SequenceEqual(requestedSources)))
            throw new InvalidOperationException("MEDICAL_REVIEW_CATALOG_CONTENT_CONFLICT");
        foreach(var source in sources)
        {
            await using(var add=new NpgsqlCommand("""
              INSERT INTO internal_test_medical_review_source(catalog_sha256,source_ref,package_sha256)
              VALUES(@c,@s,@p) ON CONFLICT DO NOTHING
              """,cn,tx))
            {
                add.Parameters.AddWithValue("c",catalog);add.Parameters.AddWithValue("s",source.SourceRef);
                add.Parameters.AddWithValue("p",source.PackageSha256);await add.ExecuteNonQueryAsync(ct);
            }
            await using var verify=new NpgsqlCommand("""
              SELECT package_sha256 FROM internal_test_medical_review_source WHERE catalog_sha256=@c AND source_ref=@s
              """,cn,tx);
            verify.Parameters.AddWithValue("c",catalog);verify.Parameters.AddWithValue("s",source.SourceRef);
            if((string?)await verify.ExecuteScalarAsync(ct)!=source.PackageSha256)
                throw new InvalidOperationException("MEDICAL_REVIEW_SOURCE_SCOPE_CONFLICT");
        }
        await tx.CommitAsync(ct);
    }

    internal async Task<MedicalSourceReviewAppendResult> AppendAsync(string catalog,string taxonomy,
        MedicalSourceReviewCommand input,CancellationToken ct=default)
    {
        if(!Sha(catalog)||!Sha(taxonomy)||!Valid(input))return new(MedicalSourceReviewOutcome.Invalid,0);
        await using var cn=await writerDataSource.OpenConnectionAsync(ct);
        await using var tx=await cn.BeginTransactionAsync(ct);
        long revision;string head;string storedTaxonomy;
        await using(var query=new NpgsqlCommand("""
          SELECT current_revision,head_sha256,taxonomy_sha256 FROM internal_test_medical_review_head
          WHERE catalog_sha256=@c FOR UPDATE
          """,cn,tx))
        {
            query.Parameters.AddWithValue("c",catalog);
            await using var reader=await query.ExecuteReaderAsync(ct);
            if(!await reader.ReadAsync(ct))return await Rollback<MedicalSourceReviewAppendResult>(tx,new(MedicalSourceReviewOutcome.ScopeDenied,0),ct);
            revision=reader.GetInt64(0);head=reader.GetString(1);storedTaxonomy=reader.GetString(2);
        }
        if(storedTaxonomy!=taxonomy)return await Rollback<MedicalSourceReviewAppendResult>(tx,new(MedicalSourceReviewOutcome.ScopeDenied,revision),ct);
        var state=await ReadLockedAsync(cn,tx,catalog,taxonomy,ct,lockHead:false);
        var replay=state.Events.FirstOrDefault(x=>x.Command.RequestId==input.RequestId);
        if(replay is not null)return await Rollback<MedicalSourceReviewAppendResult>(tx,new(replay.Command==input?
            MedicalSourceReviewOutcome.Replayed:MedicalSourceReviewOutcome.ConflictingReplay,revision),ct);
        if(input.ExpectedRevision!=revision)return await Rollback<MedicalSourceReviewAppendResult>(tx,new(MedicalSourceReviewOutcome.StaleRevision,revision),ct);
        if(!state.Sources.TryGetValue(input.SourceRef,out var package)||package!=input.PackageSha256)
            return await Rollback<MedicalSourceReviewAppendResult>(tx,new(MedicalSourceReviewOutcome.ScopeDenied,revision),ct);
        if(revision>=1024)return await Rollback<MedicalSourceReviewAppendResult>(tx,new(MedicalSourceReviewOutcome.Invalid,revision),ct);
        var next=revision+1;var json=JsonSerializer.Serialize(input);var digest=EventDigest(head,next,json);
        await using(var add=new NpgsqlCommand("""
          INSERT INTO internal_test_medical_review_event
            (catalog_sha256,revision,request_id,source_ref,package_sha256,command_json,previous_sha256,event_sha256)
          VALUES(@c,@r,@id,@s,@p,@j,@prev,@d)
          """,cn,tx))
        {
            add.Parameters.AddWithValue("c",catalog);add.Parameters.AddWithValue("r",next);
            add.Parameters.AddWithValue("id",input.RequestId);add.Parameters.AddWithValue("s",input.SourceRef);
            add.Parameters.AddWithValue("p",input.PackageSha256);add.Parameters.AddWithValue("j",json);
            add.Parameters.AddWithValue("prev",head);add.Parameters.AddWithValue("d",digest);await add.ExecuteNonQueryAsync(ct);
        }
        await using(var audit=new NpgsqlCommand("""
          INSERT INTO internal_test_medical_review_audit(catalog_sha256,revision,request_id,event_sha256)
          VALUES(@c,@r,@id,@d)
          """,cn,tx))
        {
            audit.Parameters.AddWithValue("c",catalog);audit.Parameters.AddWithValue("r",next);
            audit.Parameters.AddWithValue("id",input.RequestId);audit.Parameters.AddWithValue("d",digest);
            await audit.ExecuteNonQueryAsync(ct);
        }
        await using(var update=new NpgsqlCommand("""
          UPDATE internal_test_medical_review_head SET current_revision=@n,head_sha256=@d
          WHERE catalog_sha256=@c AND current_revision=@r AND head_sha256=@h
          """,cn,tx))
        {
            update.Parameters.AddWithValue("n",next);update.Parameters.AddWithValue("d",digest);
            update.Parameters.AddWithValue("c",catalog);update.Parameters.AddWithValue("r",revision);
            update.Parameters.AddWithValue("h",head);
            if(await update.ExecuteNonQueryAsync(ct)!=1)throw new InvalidOperationException("MEDICAL_REVIEW_HEAD_CAS_CONFLICT");
        }
        await tx.CommitAsync(ct);return new(MedicalSourceReviewOutcome.Recorded,next);
    }

    internal async Task<MedicalSourceReviewSnapshot> ReadAsync(string catalog,string taxonomy,CancellationToken ct=default)
    {
        if(!Sha(catalog)||!Sha(taxonomy))throw new ArgumentException("Invalid synthetic review scope.");
        await using var cn=await readerDataSource.OpenConnectionAsync(ct);
        await using var tx=await cn.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);
        var state=await ReadLockedAsync(cn,tx,catalog,taxonomy,ct,lockHead:false);
        await tx.CommitAsync(ct);
        return new(state.Revision,state.HeadSha256,state.Events.AsReadOnly(),false,false,false,false,false,false,false,false);
    }

    private sealed record ReadState(long Revision,string HeadSha256,List<MedicalSourceReviewEvent> Events,Dictionary<string,string> Sources);
    private static async Task<ReadState> ReadLockedAsync(NpgsqlConnection cn,NpgsqlTransaction tx,string catalog,string taxonomy,CancellationToken ct,bool lockHead)
    {
        long revision;string head;
        var headSql="""
          SELECT current_revision,head_sha256,taxonomy_sha256 FROM internal_test_medical_review_head
          WHERE catalog_sha256=@c
          """;
        await using(var q=new NpgsqlCommand(lockHead?headSql+" FOR UPDATE":headSql,cn,tx))
        {
            q.Parameters.AddWithValue("c",catalog);
            await using var r=await q.ExecuteReaderAsync(ct);
            if(!await r.ReadAsync(ct)||r.GetString(2)!=taxonomy)throw new InvalidOperationException("MEDICAL_REVIEW_SCOPE_MISSING_OR_CONFLICTED");
            revision=r.GetInt64(0);head=r.GetString(1);
        }
        var sources=new Dictionary<string,string>(StringComparer.Ordinal);
        await using(var q=new NpgsqlCommand("""
          SELECT source_ref,package_sha256 FROM internal_test_medical_review_source WHERE catalog_sha256=@c ORDER BY source_ref
          """,cn,tx))
        {
            q.Parameters.AddWithValue("c",catalog);await using var r=await q.ExecuteReaderAsync(ct);
            while(await r.ReadAsync(ct))sources.Add(r.GetString(0),r.GetString(1));
        }
        var events=new List<MedicalSourceReviewEvent>();var previous=Digest($"genesis\n{catalog}\n{taxonomy}");
        await using(var q=new NpgsqlCommand("""
          SELECT e.revision,e.request_id,e.command_json,e.previous_sha256,e.event_sha256,a.request_id,a.event_sha256
          FROM internal_test_medical_review_event e LEFT JOIN internal_test_medical_review_audit a
            ON a.catalog_sha256=e.catalog_sha256 AND a.revision=e.revision
          WHERE e.catalog_sha256=@c ORDER BY e.revision
          """,cn,tx))
        {
            q.Parameters.AddWithValue("c",catalog);await using var r=await q.ExecuteReaderAsync(ct);long n=1;
            while(await r.ReadAsync(ct))
            {
                var json=r.GetString(2);var command=JsonSerializer.Deserialize<MedicalSourceReviewCommand>(json);
                var digest=r.GetString(4);
                if(command is null||!Valid(command)||r.GetInt64(0)!=n||r.GetString(3)!=previous||
                   EventDigest(previous,n,json)!=digest||r.IsDBNull(5)||r.GetGuid(1)!=r.GetGuid(5)||
                   r.GetString(6)!=digest||!sources.TryGetValue(command.SourceRef,out var package)||package!=command.PackageSha256)
                    throw new InvalidOperationException("MEDICAL_REVIEW_CHAIN_OR_AUDIT_TAMPERED");
                events.Add(new(n,command,previous,digest));previous=digest;n++;
            }
        }
        if(events.Count!=revision||previous!=head)throw new InvalidOperationException("MEDICAL_REVIEW_HEAD_DIVERGENCE");
        return new(revision,head,events,sources);
    }

    private static bool Valid(MedicalSourceReviewSubmission x)=>x is not null&&x.RequestId!=Guid.Empty&&x.ExpectedRevision>=0&&
        TestRef(x.SourceRef)&&Sha(x.PackageSha256)&&Lanes.Contains(x.Lane)&&Recommendations.Contains(x.Recommendation)&&
        Ref(x.ReviewerRefClaimed,"reviewer:")&&Ref(x.EvidenceRefClaimed,"evidence:");
    private static bool Valid(MedicalSourceReviewCommand x)=>x is not null&&
        Valid(new MedicalSourceReviewSubmission(x.RequestId,x.ExpectedRevision,x.SourceRef,x.PackageSha256,x.Lane,
            x.Recommendation,x.ReviewerRefClaimed,x.EvidenceRefClaimed))&&Sha(x.EvidencePackageSha256);
    private static bool TestRef(string x)=>x is not null&&Regex.IsMatch(x,@"^test:[A-Za-z0-9_:-]{1,120}$",RegexOptions.CultureInvariant);
    private static bool Ref(string x,string prefix)=>x is not null&&x.StartsWith(prefix,StringComparison.Ordinal)&&
        x.Length>prefix.Length&&x.Length<=129&&x.AsSpan(prefix.Length).ToString().All(c=>char.IsAsciiLetterOrDigit(c)||c is '_' or ':' or '-');
    private static bool Sha(string x)=>x is not null&&x.Length==64&&x.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static string Digest(string s)=>Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
    private static string EventDigest(string prev,long revision,string json)=>Digest($"event\n{prev}\n{revision}\n{json}");
    private static async Task<T> Rollback<T>(NpgsqlTransaction tx,T result,CancellationToken ct){await tx.RollbackAsync(ct);return result;}
}
