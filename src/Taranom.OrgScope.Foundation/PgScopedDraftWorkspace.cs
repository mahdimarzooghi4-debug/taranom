using Npgsql;

namespace Taranom.OrgScope.Foundation;

// TRN-TECH-132: a real transactional *technical draft* workspace, confined to
// the two ephemeral PostgreSQL organization logins. No HTTP, business approval,
// person, case, clinical record, sponsor enrollment or financial action exists.
internal enum DraftWriteStatus
{
    Recorded,
    Replayed,
    ConflictingReplay,
    InvalidRequest
}

internal sealed record ScopedDraft(string ScopeRef, string ItemRef, string SourceRef);
internal sealed record DraftWriteResult(DraftWriteStatus Status, ScopedDraft? Draft);
internal sealed record DraftPage(IReadOnlyList<ScopedDraft> Items,
    string? NextAfterItemRef);

// A caller can supply only opaque test item and source references. The
// organization scope comes exclusively from the current database session_user
// via owner-controlled binding, never from UI/header/API or a client-supplied
// scopeRef. Session is revalidated for every operation, including reads.
internal sealed class PgScopedDraftWorkspace(NpgsqlDataSource database)
{
    private static bool IsTestRef(string? value) =>
        value is { Length: > 5 and <= 128 } &&
        value.StartsWith("test:", StringComparison.Ordinal) &&
        value.AsSpan(5).ToArray().All(c =>
            char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or ':');

    private static void RequireEphemeral()
    {
        if (Environment.GetEnvironmentVariable("TARANOM_CENTER_DEMO_TEST_ONLY") !=
            "CI_EPHEMERAL_ONLY")
            throw new InvalidOperationException("ORG_DRAFT_TEST_ONLY");
    }

    private static async Task<string> ScopeAsync(NpgsqlConnection conn,
        NpgsqlTransaction? tx = null)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT current_database(),current_user,session_user,
                   public.internal_test_org_scope_for_login(),
                   rolsuper,rolbypassrls
            FROM pg_roles WHERE rolname=current_user
            """, conn, tx);
        await using var row = await cmd.ExecuteReaderAsync();
        if (!await row.ReadAsync() ||
            row.GetString(0) != "taranom_sprint64_center" ||
            row.GetString(1) != row.GetString(2) ||
            row.GetBoolean(4) || row.GetBoolean(5))
            throw new InvalidOperationException("ORG_DRAFT_UNTRUSTED_PRINCIPAL");

        string role = row.GetString(1);
        string? scope = row.IsDBNull(3) ? null : row.GetString(3);
        if ((role == PgSyntheticOrganizationScope.SupportRole &&
                scope == PgSyntheticOrganizationScope.SupportRef) ||
            (role == PgSyntheticOrganizationScope.CorporateRole &&
                scope == PgSyntheticOrganizationScope.CorporateRef))
            return scope;
        throw new InvalidOperationException("ORG_DRAFT_SCOPE_UNVERIFIABLE");
    }

    internal async Task<DraftWriteResult> RecordAsync(
        string? itemRef, string? sourceRef, CancellationToken ct = default)
    {
        RequireEphemeral();
        if (!IsTestRef(itemRef) || !IsTestRef(sourceRef))
            return new(DraftWriteStatus.InvalidRequest, null);

        await using var conn = await database.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var scope = await ScopeAsync(conn, tx);

        // The composite primary key and RLS WITH CHECK protect the write at DB
        // level. INSERT-on-conflict-no-op + SELECT in READ COMMITTED enables
        // safe exact replay even with concurrent independent connections.
        bool inserted;
        await using (var create = new NpgsqlCommand("""
            INSERT INTO public.internal_test_org_draft_item
                (scope_ref,item_ref,source_ref)
            VALUES (@scope,@item,@source)
            ON CONFLICT (scope_ref,item_ref) DO NOTHING
            RETURNING item_ref
            """, conn, tx))
        {
            create.Parameters.AddWithValue("scope", scope);
            create.Parameters.AddWithValue("item", itemRef!);
            create.Parameters.AddWithValue("source", sourceRef!);
            inserted = await create.ExecuteScalarAsync(ct) is string;
        }

        string? actualSource;
        await using (var verify = new NpgsqlCommand("""
            SELECT source_ref FROM public.internal_test_org_draft_item
            WHERE scope_ref=@scope AND item_ref=@item
            """, conn, tx))
        {
            verify.Parameters.AddWithValue("scope", scope);
            verify.Parameters.AddWithValue("item", itemRef!);
            actualSource = (string?)await verify.ExecuteScalarAsync(ct);
        }
        if (!IsTestRef(actualSource))
            throw new InvalidOperationException("ORG_DRAFT_SOURCE_UNVERIFIABLE");

        var draft = new ScopedDraft(scope,itemRef!,actualSource!);
        DraftWriteStatus status = inserted ? DraftWriteStatus.Recorded :
            string.Equals(actualSource,sourceRef,StringComparison.Ordinal)
                ? DraftWriteStatus.Replayed : DraftWriteStatus.ConflictingReplay;
        await tx.CommitAsync(ct);
        return new(status,draft);
    }

    internal async Task<ScopedDraft?> ReadAsync(string? itemRef,
        CancellationToken ct = default)
    {
        RequireEphemeral();
        if (!IsTestRef(itemRef))
            throw new ArgumentException("ORG_DRAFT_INVALID_ITEM_REF",
                nameof(itemRef));
        await using var conn = await database.OpenConnectionAsync(ct);
        var scope = await ScopeAsync(conn);
        await using var cmd = new NpgsqlCommand("""
            SELECT source_ref FROM public.internal_test_org_draft_item
            WHERE scope_ref=@scope AND item_ref=@item
            """,conn);
        cmd.Parameters.AddWithValue("scope",scope);
        cmd.Parameters.AddWithValue("item",itemRef!);
        var actual = (string?)await cmd.ExecuteScalarAsync(ct);
        if (actual is null) return null;
        if (!IsTestRef(actual))
            throw new InvalidOperationException("ORG_DRAFT_SOURCE_UNVERIFIABLE");
        return new ScopedDraft(scope,itemRef!,actual);
    }

    internal async Task<DraftPage> ListAsync(string? afterItemRef, int limit,
        CancellationToken ct = default)
    {
        RequireEphemeral();
        if (limit is < 1 or > 20 ||
            (afterItemRef is not null && !IsTestRef(afterItemRef)))
            throw new ArgumentException("ORG_DRAFT_INVALID_CURSOR_OR_LIMIT");
        await using var conn = await database.OpenConnectionAsync(ct);
        // Scope binding and listing see the same PostgreSQL snapshot.
        await using var tx = await conn.BeginTransactionAsync(
            System.Data.IsolationLevel.RepeatableRead, ct);
        await using (var mode = new NpgsqlCommand("SET TRANSACTION READ ONLY",
            conn,tx))
            await mode.ExecuteNonQueryAsync(ct);
        var scope = await ScopeAsync(conn, tx);
        var items = new List<ScopedDraft>();
        await using (var cmd = new NpgsqlCommand("""
            SELECT item_ref,source_ref
            FROM public.internal_test_org_draft_item
            WHERE scope_ref=@scope AND
                  (@after='' OR item_ref COLLATE "C" > @after COLLATE "C")
            ORDER BY item_ref COLLATE "C"
            LIMIT @count
            """,conn,tx))
        {
            cmd.Parameters.AddWithValue("scope",scope);
            cmd.Parameters.AddWithValue("after",afterItemRef ?? "");
            cmd.Parameters.AddWithValue("count",limit+1);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                string item=reader.GetString(0),source=reader.GetString(1);
                if (!IsTestRef(item) || !IsTestRef(source))
                    throw new InvalidOperationException(
                        "ORG_DRAFT_PAGE_UNVERIFIABLE");
                items.Add(new ScopedDraft(scope,item,source));
            }
        }
        await tx.CommitAsync(ct);
        bool more = items.Count > limit;
        return new DraftPage(items.Take(limit).ToArray(),
            more ? items[limit-1].ItemRef : null);
    }
}
