using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;

namespace Taranom.SyntheticFoundation;

// A test-only PostgreSQL-backed boundary; no API routes, clinical data or production adapters.
public sealed class SyntheticConsentStore
{
    private readonly NpgsqlDataSource dataSource;

    // Not constructible from any deployable application assembly; smoke tests are a friend
    // assembly, and cannot be treated as a real identity/consent provider.
    internal SyntheticConsentStore(NpgsqlDataSource testDataSource)
    {
        dataSource = testDataSource;
    }

    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS synthetic_consent_stream (
            tenant_ref text NOT NULL, subject_ref text NOT NULL, purpose text NOT NULL, scope_ref text NOT NULL,
            version integer NOT NULL DEFAULT 0 CHECK (version >= 0),
            PRIMARY KEY (tenant_ref, subject_ref, purpose, scope_ref)
        );
        CREATE TABLE IF NOT EXISTS synthetic_audit (
            audit_ref uuid PRIMARY KEY,
            tenant_ref text NOT NULL,
            actor_ref text NOT NULL,
            action_code text NOT NULL,
            decision_code text NOT NULL,
            correlation_ref text NOT NULL,
            occurred_at timestamptz NOT NULL DEFAULT now()
        );
        CREATE TABLE IF NOT EXISTS synthetic_consent_evidence (
            tenant_ref text NOT NULL, subject_ref text NOT NULL, purpose text NOT NULL, scope_ref text NOT NULL,
            version integer NOT NULL CHECK (version > 0),
            granted boolean NOT NULL, actor_ref text NOT NULL,
            audit_ref uuid NOT NULL REFERENCES synthetic_audit(audit_ref),
            previous_version integer NOT NULL CHECK (previous_version >= 0),
            PRIMARY KEY (tenant_ref, subject_ref, purpose, scope_ref, version)
        );
        CREATE TABLE IF NOT EXISTS synthetic_request_replay (
            tenant_ref text NOT NULL, subject_ref text NOT NULL, purpose text NOT NULL, scope_ref text NOT NULL,
            actor_ref text NOT NULL, operation text NOT NULL, request_ref text NOT NULL,
            input_hash text NOT NULL, resulting_version integer NOT NULL,
            PRIMARY KEY (tenant_ref, subject_ref, purpose, scope_ref, actor_ref, operation, request_ref)
        );
        CREATE OR REPLACE FUNCTION synthetic_reject_audit_change() RETURNS trigger
        LANGUAGE plpgsql AS $$
        BEGIN
            RAISE EXCEPTION 'AUDIT_APPEND_ONLY';
        END;
        $$;
        DROP TRIGGER IF EXISTS synthetic_audit_immutable ON synthetic_audit;
        CREATE TRIGGER synthetic_audit_immutable
            BEFORE UPDATE OR DELETE ON synthetic_audit
            FOR EACH ROW EXECUTE FUNCTION synthetic_reject_audit_change();
        DROP TRIGGER IF EXISTS synthetic_evidence_immutable ON synthetic_consent_evidence;
        CREATE TRIGGER synthetic_evidence_immutable
            BEFORE UPDATE OR DELETE ON synthetic_consent_evidence
            FOR EACH ROW EXECUTE FUNCTION synthetic_reject_audit_change();
        """;

    public async Task InitializeTestSchemaAsync(CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand(SchemaSql, connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<ConsentMutationResult> ChangeAsync(
        SyntheticPrincipal principal, string subject, string tenant, SyntheticPurpose purpose,
        string scope, int expectedVersion, string requestRef, bool grant,
        CancellationToken ct = default)
    {
        var access = SyntheticAccessPolicy.Evaluate(principal, subject, tenant, "CHANGE_OWN_TEST_CONSENT");
        if (!access.Allowed || !SyntheticAccessPolicy.IsSynthetic(scope) ||
            !SyntheticAccessPolicy.IsSynthetic(requestRef) || expectedVersion < 0)
            throw new InvalidOperationException("DENY");
        var purposeCode = purpose.ToString();
        if (!Enum.IsDefined(purpose)) throw new InvalidOperationException("INVALID_PURPOSE");
        var operation = grant ? "GRANT_TEST" : "WITHDRAW_TEST";
        var serialized = JsonSerializer.Serialize(new { principal.Actor, subject, tenant,
            purposeCode, scope, expectedVersion, requestRef, operation });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(serialized)));

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await ExecuteAsync(connection, transaction, """
            INSERT INTO synthetic_consent_stream(tenant_ref,subject_ref,purpose,scope_ref,version)
            VALUES (@tenant,@subject,@purpose,@scope,0) ON CONFLICT DO NOTHING
            """, ct, ("tenant", tenant), ("subject", subject), ("purpose", purposeCode), ("scope", scope));

        var versionObj = await ScalarAsync(connection, transaction, """
            SELECT version FROM synthetic_consent_stream
            WHERE tenant_ref=@tenant AND subject_ref=@subject AND purpose=@purpose AND scope_ref=@scope
            FOR UPDATE
            """, ct, ("tenant", tenant), ("subject", subject), ("purpose", purposeCode), ("scope", scope));
        var current = Convert.ToInt32(versionObj);

        await using (var lookup = NewCommand(connection, transaction, """
            SELECT input_hash, resulting_version FROM synthetic_request_replay
            WHERE tenant_ref=@tenant AND subject_ref=@subject AND purpose=@purpose AND scope_ref=@scope
              AND actor_ref=@actor AND operation=@operation AND request_ref=@request
            """,
            ("tenant", tenant), ("subject", subject), ("purpose", purposeCode), ("scope", scope),
            ("actor", principal.Actor), ("operation", operation), ("request", requestRef)))
        await using (var reader = await lookup.ExecuteReaderAsync(ct))
        {
            if (await reader.ReadAsync(ct))
            {
                var oldHash = reader.GetString(0);
                var replayVersion = reader.GetInt32(1);
                if (!string.Equals(oldHash, hash, StringComparison.Ordinal))
                    throw new InvalidOperationException("IDEMPOTENCY_CONFLICT");
                await reader.CloseAsync();
                await transaction.CommitAsync(ct);
                return new ConsentMutationResult(replayVersion, true);
            }
        }

        if (current != expectedVersion) throw new InvalidOperationException("VERSION_CONFLICT");
        var next = checked(current + 1);
        var auditRef = Guid.NewGuid();
        await ExecuteAsync(connection, transaction, """
            INSERT INTO synthetic_audit(audit_ref,tenant_ref,actor_ref,action_code,decision_code,correlation_ref)
            VALUES (@audit,@tenant,@actor,@action,'RECORDED',@correlation)
            """, ct, ("audit", auditRef), ("tenant", tenant), ("actor", principal.Actor),
            ("action", operation), ("correlation", requestRef));
        await ExecuteAsync(connection, transaction, """
            INSERT INTO synthetic_consent_evidence
                (tenant_ref,subject_ref,purpose,scope_ref,version,granted,actor_ref,audit_ref,previous_version)
            VALUES (@tenant,@subject,@purpose,@scope,@version,@granted,@actor,@audit,@previous)
            """, ct, ("tenant", tenant), ("subject", subject), ("purpose", purposeCode), ("scope", scope),
            ("version", next), ("granted", grant), ("actor", principal.Actor),
            ("audit", auditRef), ("previous", current));
        await ExecuteAsync(connection, transaction, """
            UPDATE synthetic_consent_stream SET version=@next
            WHERE tenant_ref=@tenant AND subject_ref=@subject AND purpose=@purpose AND scope_ref=@scope
            """, ct, ("next", next), ("tenant", tenant), ("subject", subject),
            ("purpose", purposeCode), ("scope", scope));
        await ExecuteAsync(connection, transaction, """
            INSERT INTO synthetic_request_replay
                (tenant_ref,subject_ref,purpose,scope_ref,actor_ref,operation,request_ref,input_hash,resulting_version)
            VALUES (@tenant,@subject,@purpose,@scope,@actor,@operation,@request,@hash,@next)
            """, ct, ("tenant", tenant), ("subject", subject), ("purpose", purposeCode), ("scope", scope),
            ("actor", principal.Actor), ("operation", operation),
            ("request", requestRef), ("hash", hash), ("next", next));
        await transaction.CommitAsync(ct);
        return new ConsentMutationResult(next, false);
    }

    public async Task<bool> EvaluateAsync(SyntheticPrincipal principal,
        string subject, string tenant, SyntheticPurpose purpose, string scope,
        CancellationToken ct = default)
    {
        if (!SyntheticAccessPolicy.Evaluate(principal, subject, tenant, "READ_OWN_TEST_CONSENT").Allowed ||
            !SyntheticAccessPolicy.IsSynthetic(scope) || !Enum.IsDefined(purpose))
            return false;
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = NewCommand(connection, null, """
            SELECT granted FROM synthetic_consent_evidence
            WHERE tenant_ref=@tenant AND subject_ref=@subject AND purpose=@purpose AND scope_ref=@scope
            ORDER BY version DESC LIMIT 1
            """, ("tenant", tenant), ("subject", subject), ("purpose", purpose.ToString()), ("scope", scope));
        var value = await command.ExecuteScalarAsync(ct);
        return value is bool granted && granted;
    }

    public async Task<int> AuditCountAsync(CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM synthetic_audit", connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
    }

    private static NpgsqlCommand NewCommand(NpgsqlConnection connection,
        NpgsqlTransaction? transaction, string sql, params (string Name, object Value)[] parameters)
    {
        var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        return command;
    }

    private static async Task<int> ExecuteAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, string sql, CancellationToken ct,
        params (string Name, object Value)[] args)
    {
        await using var command = NewCommand(connection, transaction, sql, args);
        return await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, string sql, CancellationToken ct,
        params (string Name, object Value)[] args)
    {
        await using var command = NewCommand(connection, transaction, sql, args);
        return await command.ExecuteScalarAsync(ct);
    }
}
