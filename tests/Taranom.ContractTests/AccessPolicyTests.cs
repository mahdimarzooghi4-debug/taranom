using Taranom.SyntheticFoundation;
using Xunit;

namespace Taranom.ContractTests;

// Independent named assertions; intentionally no real provider, token, API or health record.
public sealed class AccessPolicyTests
{
    private static SyntheticPrincipal Alice(string role = "MEMBER_TEST", bool trusted = true) =>
        new("test:actor-a", "test:member-a", "test:tenant-a", role, trusted);

    [Fact]
    public void ID01_AnonymousIsDenied() =>
        Assert.False(SyntheticAccessPolicy.Evaluate(null, "test:member-a", "test:tenant-a", "READ_OWN_TEST_PROFILE").Allowed);

    [Fact]
    public void ID02_CrossMemberIsDenied() =>
        Assert.False(SyntheticAccessPolicy.Evaluate(Alice(), "test:member-b", "test:tenant-a", "READ_OWN_TEST_PROFILE").Allowed);

    [Fact]
    public void ID03_ExplicitOwnScopeIsAllowed() =>
        Assert.True(SyntheticAccessPolicy.Evaluate(Alice(), "test:member-a", "test:tenant-a", "READ_OWN_TEST_PROFILE").Allowed);

    [Fact]
    public void ID04_UntrustedAssertionIsDenied() =>
        Assert.Equal("UNTRUSTED_ORIGIN", SyntheticAccessPolicy.Evaluate(Alice(trusted: false),
            "test:member-a", "test:tenant-a", "READ_OWN_TEST_PROFILE").Reason);

    [Fact]
    public void ID05_CrossTenantIsDenied() =>
        Assert.Equal("SUBJECT_SCOPE_CONFLICT", SyntheticAccessPolicy.Evaluate(Alice(),
            "test:member-a", "test:tenant-b", "READ_OWN_TEST_PROFILE").Reason);

    [Fact]
    public void ID06_SponsorCannotReadMemberProfile() =>
        Assert.Equal("ROLE_DENIED", SyntheticAccessPolicy.Evaluate(Alice(role: "SPONSOR_TEST"),
            "test:member-a", "test:tenant-a", "READ_OWN_TEST_PROFILE").Reason);

    [Theory]
    [InlineData("")]
    [InlineData("real:member-a")]
    [InlineData("test:member with spaces")]
    public void ID07_InvalidMemberReferenceIsDenied(string member) =>
        Assert.Equal("INVALID_SYNTHETIC_REFERENCE", SyntheticAccessPolicy.Evaluate(Alice(),
            member, "test:tenant-a", "READ_OWN_TEST_PROFILE").Reason);

    [Fact]
    public void ID08_UnknownActionIsDenied() =>
        Assert.Equal("ACTION_DENIED", SyntheticAccessPolicy.Evaluate(Alice(),
            "test:member-a", "test:tenant-a", "UNREVIEWED_FINANCE_ACTION").Reason);

    [Fact]
    public void X06_OnlyExplicitSyntheticOriginCanGrant() =>
        Assert.False(SyntheticAccessPolicy.Evaluate(Alice(trusted: false),
            "test:member-a", "test:tenant-a", "CHANGE_OWN_TEST_CONSENT").Allowed);

    [Fact]
    public void X07_SponsorWithSameTenantCannotImpersonateMember() =>
        Assert.False(SyntheticAccessPolicy.Evaluate(Alice(role: "SPONSOR_TEST"),
            "test:member-a", "test:tenant-a", "READ_OWN_TEST_CONSENT").Allowed);
}
