using Npgsql;

namespace Taranom.Appointments.Api;

internal enum CenterAccessGrantOutcome
{
    Applied,
    Replayed,
    Conflict,
    NotFound,
    Invalid
}

internal sealed record CenterAccessGrantRecord(
    string TargetSubject,
    string Capability,
    DateTimeOffset CreatedAt);

internal sealed class CenterAccessGrantService(NpgsqlDataSource dataSource)
{
    private static readonly HashSet<string> DelegableCapabilities = new(StringComparer.Ordinal)
    {
        "CenterAccessGrantsManage",
        "CenterRosterManage",
        "CenterAppointmentsManage",
        "CenterRevenueRead"
    };

    internal async Task<IReadOnlyList<CenterAccessGrantRecord>> ListAsync(
        string issuer,
        string centerRef,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT subject, capability, created_at
            FROM taranom_panel_access_grant
            WHERE issuer = @issuer AND scope_ref = @center_ref
              AND grant_state = 'active'
              AND capability = ANY(@capabilities)
            ORDER BY capability, subject
            """, connection);
        command.Parameters.AddWithValue("issuer", issuer);
        command.Parameters.AddWithValue("center_ref", centerRef);
        command.Parameters.AddWithValue("capabilities", DelegableCapabilities.ToArray());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var results = new List<CenterAccessGrantRecord>();
        while (await reader.ReadAsync(cancellationToken))
            results.Add(new CenterAccessGrantRecord(
                reader.GetString(0), reader.GetString(1),
                new DateTimeOffset(reader.GetDateTime(2), TimeSpan.Zero)));
        return results;
    }

    internal async Task<CenterAccessGrantOutcome> ApplyAsync(
        string actorIssuer,
        string actorSubject,
        string centerRef,
        CenterAccessGrantMutation mutation,
        bool revoke,
        CancellationToken cancellationToken)
    {
        if (!IsValidReference(actorIssuer, 512) || !IsValidReference(actorSubject, 256) ||
            !IsValidReference(centerRef, 256) || !IsValidReference(mutation.TargetSubject, 256) ||
            string.Equals(actorSubject, mutation.TargetSubject, StringComparison.Ordinal) ||
            mutation.RequestId == Guid.Empty || !DelegableCapabilities.Contains(mutation.Capability) ||
            string.IsNullOrWhiteSpace(mutation.Reason) || mutation.Reason.Length > 500 ||
            mutation.Reason.Any(char.IsControl))
            return CenterAccessGrantOutcome.Invalid;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var lockCommand = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended(@request_key, 0))",
            connection, transaction))
        {
            lockCommand.Parameters.AddWithValue("request_key",
                $"{actorIssuer}\n{actorSubject}\n{mutation.RequestId:D}");
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        // A center-level grant must never be issued against an arbitrary
        // opaque string. Require the Admin-defined center row and hold it
        // through this transaction before replay or any grant mutation.
        await using (var canonical = new NpgsqlCommand("""
            SELECT 1 FROM taranom_canonical_center
            WHERE center_ref=@center FOR SHARE
            """, connection, transaction))
        {
            canonical.Parameters.AddWithValue("center", centerRef);
            if (await canonical.ExecuteScalarAsync(cancellationToken) is null)
                return CenterAccessGrantOutcome.NotFound;
        }

        string[]? existingEvent = null;
        await using (var replayCommand = new NpgsqlCommand("""
            SELECT target_issuer, target_subject, capability, scope_ref, action, reason
            FROM taranom_panel_access_grant_audit
            WHERE actor_issuer = @actor_issuer AND actor_subject = @actor_subject
              AND request_id = @request_id
            """, connection, transaction))
        {
            replayCommand.Parameters.AddWithValue("actor_issuer", actorIssuer);
            replayCommand.Parameters.AddWithValue("actor_subject", actorSubject);
            replayCommand.Parameters.AddWithValue("request_id", mutation.RequestId);
            await using var reader = await replayCommand.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
                existingEvent = Enumerable.Range(0, 6).Select(reader.GetString).ToArray();
        }

        if (existingEvent is not null)
        {
            var sameCommand = existingEvent[0] == actorIssuer &&
                existingEvent[1] == mutation.TargetSubject &&
                existingEvent[2] == mutation.Capability &&
                existingEvent[3] == centerRef &&
                existingEvent[4] == (revoke ? "revoked" : "granted") &&
                existingEvent[5] == mutation.Reason;
            await transaction.CommitAsync(cancellationToken);
            return sameCommand ? CenterAccessGrantOutcome.Replayed : CenterAccessGrantOutcome.Conflict;
        }

        if (revoke)
        {
            await using var revokeCommand = new NpgsqlCommand("""
                UPDATE taranom_panel_access_grant
                SET grant_state = 'revoked', grant_not_revoked = false
                WHERE issuer = @target_issuer AND subject = @target_subject
                  AND capability = @capability AND scope_ref = @center_ref
                  AND grant_state = 'active'
                """, connection, transaction);
            AddMutationParameters(revokeCommand, actorIssuer, centerRef, mutation);
            var changed = await revokeCommand.ExecuteNonQueryAsync(cancellationToken);
            if (changed == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return CenterAccessGrantOutcome.NotFound;
            }
        }
        else
        {
            await using var grantCommand = new NpgsqlCommand("""
                INSERT INTO taranom_panel_access_grant
                    (issuer, subject, capability, scope_ref, grant_state,
                     session_current, subject_mapping_current, permission_version_current,
                     grant_not_revoked, audit_ready)
                VALUES (@target_issuer, @target_subject, @capability, @center_ref, 'active',
                        false, true, true, true, true)
                ON CONFLICT (issuer, subject, capability, scope_ref)
                DO UPDATE SET grant_state = 'active', session_current = false,
                    subject_mapping_current = true, permission_version_current = true,
                    grant_not_revoked = true, audit_ready = true
                """, connection, transaction);
            AddMutationParameters(grantCommand, actorIssuer, centerRef, mutation);
            await grantCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var auditCommand = new NpgsqlCommand("""
            INSERT INTO taranom_panel_access_grant_audit
                (event_id, request_id, actor_issuer, actor_subject,
                 target_issuer, target_subject, capability, scope_ref, action, reason)
            VALUES (@event_id, @request_id, @actor_issuer, @actor_subject,
                    @target_issuer, @target_subject, @capability, @center_ref, @action, @reason)
            """, connection, transaction))
        {
            auditCommand.Parameters.AddWithValue("event_id", Guid.NewGuid());
            auditCommand.Parameters.AddWithValue("request_id", mutation.RequestId);
            auditCommand.Parameters.AddWithValue("actor_issuer", actorIssuer);
            auditCommand.Parameters.AddWithValue("actor_subject", actorSubject);
            auditCommand.Parameters.AddWithValue("target_issuer", actorIssuer);
            auditCommand.Parameters.AddWithValue("target_subject", mutation.TargetSubject);
            auditCommand.Parameters.AddWithValue("capability", mutation.Capability);
            auditCommand.Parameters.AddWithValue("center_ref", centerRef);
            auditCommand.Parameters.AddWithValue("action", revoke ? "revoked" : "granted");
            auditCommand.Parameters.AddWithValue("reason", mutation.Reason);
            await auditCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return CenterAccessGrantOutcome.Applied;
    }

    private static void AddMutationParameters(
        NpgsqlCommand command,
        string issuer,
        string centerRef,
        CenterAccessGrantMutation mutation)
    {
        command.Parameters.AddWithValue("target_issuer", issuer);
        command.Parameters.AddWithValue("target_subject", mutation.TargetSubject);
        command.Parameters.AddWithValue("capability", mutation.Capability);
        command.Parameters.AddWithValue("center_ref", centerRef);
    }

    private static bool IsValidReference(string value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength &&
        !value.Any(char.IsControl);
}

public sealed record CenterAccessGrantMutation(
    Guid RequestId,
    string TargetSubject,
    string Capability,
    string Reason);
