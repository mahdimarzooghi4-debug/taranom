using System.Collections.Immutable;
using System.Security.Cryptography;

namespace Taranom.MemberGovernance;

// Synthetic test-only record of a source-issued prescription copy. The payload
// is opaque: this contract does not author, interpret, validate or dispense it.
internal readonly record struct SyntheticPrescriptionCopyCommand(
    Guid RequestId,
    long ExpectedRevision,
    string MemberRef,
    string PrescriptionRef,
    string IssuerRef,
    string SourceRevision,
    DateTimeOffset IssuedAt,
    ImmutableArray<byte> DocumentBytes,
    string DocumentSha256);

internal readonly record struct SyntheticPrescriptionCopyVersion(
    long Revision,
    Guid RequestId,
    string MemberRef,
    string PrescriptionRef,
    string IssuerRef,
    string SourceRevision,
    DateTimeOffset IssuedAt,
    ImmutableArray<byte> DocumentBytes,
    string DocumentSha256);

internal enum SyntheticPrescriptionCopyOutcome
{
    Invalid = 1,
    RecordedCopy = 2,
    Replayed = 3,
    ConflictingReplay = 4,
    StaleRevision = 5,
    ScopeConflict = 6
}

internal sealed class SyntheticPrescriptionCopySnapshot
{
    internal static SyntheticPrescriptionCopySnapshot Empty { get; } =
        new(string.Empty, string.Empty, 0,
            ImmutableArray<SyntheticPrescriptionCopyVersion>.Empty);

    private SyntheticPrescriptionCopySnapshot(
        string memberRef,
        string prescriptionRef,
        long revision,
        ImmutableArray<SyntheticPrescriptionCopyVersion> history)
    {
        MemberRef = memberRef;
        PrescriptionRef = prescriptionRef;
        Revision = revision;
        History = history;
    }

    internal string MemberRef { get; }
    internal string PrescriptionRef { get; }
    internal long Revision { get; }
    internal ImmutableArray<SyntheticPrescriptionCopyVersion> History { get; }

    internal SyntheticPrescriptionCopySnapshot Append(
        SyntheticPrescriptionCopyCommand command)
    {
        var bytes = ImmutableArray.CreateRange(command.DocumentBytes);
        var nextRevision = checked(Revision + 1);
        var version = new SyntheticPrescriptionCopyVersion(
            nextRevision,
            command.RequestId,
            command.MemberRef,
            command.PrescriptionRef,
            command.IssuerRef,
            command.SourceRevision,
            command.IssuedAt,
            bytes,
            command.DocumentSha256);
        return new SyntheticPrescriptionCopySnapshot(
            command.MemberRef,
            command.PrescriptionRef,
            nextRevision,
            History.Add(version));
    }
}

internal readonly record struct SyntheticPrescriptionCopyChange(
    SyntheticPrescriptionCopyOutcome Outcome,
    SyntheticPrescriptionCopySnapshot Snapshot);

// This bounded synthetic reducer preserves source-provided document bytes and
// revisions. test: references and a matching hash prove neither issuer identity
// nor prescription validity, patient consent, clinical authority or legality.
internal static class SyntheticPrescriptionCopyDomain
{
    private const int MaximumDocumentBytes = 4 * 1024 * 1024;
    internal const int MaximumHistoryVersions = 16;
    internal const long MaximumHistoryDocumentBytes = 16L * 1024 * 1024;

    internal static SyntheticPrescriptionCopyChange Apply(
        SyntheticPrescriptionCopySnapshot? current,
        SyntheticPrescriptionCopyCommand input)
    {
        var snapshot = current ?? SyntheticPrescriptionCopySnapshot.Empty;
        if (input.RequestId == Guid.Empty ||
            input.ExpectedRevision < 0 ||
            input.ExpectedRevision == long.MaxValue ||
            !TestRef(input.MemberRef) ||
            !TestRef(input.PrescriptionRef) ||
            !TestRef(input.IssuerRef) ||
            !TestRef(input.SourceRevision) ||
            input.IssuedAt == default ||
            input.IssuedAt.Offset != TimeSpan.Zero ||
            input.DocumentBytes.IsDefaultOrEmpty ||
            input.DocumentBytes.Length > MaximumDocumentBytes ||
            !ValidHash(input.DocumentSha256) ||
            !HasMatchingHash(input.DocumentBytes, input.DocumentSha256))
            return new(SyntheticPrescriptionCopyOutcome.Invalid, snapshot);

        if (snapshot.Revision > 0 &&
            (!string.Equals(snapshot.MemberRef, input.MemberRef, StringComparison.Ordinal) ||
             !string.Equals(snapshot.PrescriptionRef, input.PrescriptionRef, StringComparison.Ordinal)))
            return new(SyntheticPrescriptionCopyOutcome.ScopeConflict, snapshot);

        foreach (var prior in snapshot.History)
        {
            if (prior.RequestId != input.RequestId) continue;
            return new(Equivalent(prior, input)
                ? SyntheticPrescriptionCopyOutcome.Replayed
                : SyntheticPrescriptionCopyOutcome.ConflictingReplay, snapshot);
        }

        if (input.ExpectedRevision != snapshot.Revision)
            return new(SyntheticPrescriptionCopyOutcome.StaleRevision, snapshot);

        long historyBytes = 0;
        foreach (var version in snapshot.History)
            historyBytes += version.DocumentBytes.Length;
        if (snapshot.History.Length >= MaximumHistoryVersions ||
            historyBytes + input.DocumentBytes.Length > MaximumHistoryDocumentBytes)
            return new(SyntheticPrescriptionCopyOutcome.Invalid, snapshot);

        return new(SyntheticPrescriptionCopyOutcome.RecordedCopy,
            snapshot.Append(input));
    }

    private static bool Equivalent(
        SyntheticPrescriptionCopyVersion prior,
        SyntheticPrescriptionCopyCommand input) =>
        prior.RequestId == input.RequestId &&
        prior.MemberRef == input.MemberRef &&
        prior.PrescriptionRef == input.PrescriptionRef &&
        prior.IssuerRef == input.IssuerRef &&
        prior.SourceRevision == input.SourceRevision &&
        prior.IssuedAt == input.IssuedAt &&
        prior.DocumentSha256 == input.DocumentSha256 &&
        prior.DocumentBytes.AsSpan().SequenceEqual(input.DocumentBytes.AsSpan());

    private static bool HasMatchingHash(
        ImmutableArray<byte> bytes, string expected)
    {
        var actual = Convert.ToHexString(SHA256.HashData(bytes.AsSpan())).ToLowerInvariant();
        return string.Equals(actual, expected, StringComparison.Ordinal);
    }

    private static bool ValidHash(string? value) =>
        value is { Length: 64 } &&
        value.All(ch => ch is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool TestRef(string? value) =>
        value is { Length: > 5 and <= 128 } &&
        value.StartsWith("test:", StringComparison.Ordinal) &&
        value.AsSpan(5).ToArray().All(ch =>
            char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or ':');
}
