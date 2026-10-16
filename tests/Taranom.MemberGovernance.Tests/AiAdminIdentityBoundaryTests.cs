using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Taranom.MemberGovernance;
using Xunit;

namespace Taranom.MemberGovernance.Tests;

// A generated private RSA key is used ONLY in isolated CI test fixtures.
// Its public half is an illustrative trust anchor, not a real OIDC IdP.
public sealed class AiAdminIdentityBoundaryTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

    private sealed class Fixture : IDisposable
    {
        private readonly RSA _signer = RSA.Create(2048);
        public readonly AiPinnedIdentityTrust Trust;
        public Fixture() => Trust = new(
            "https://issuer.example.invalid/test",
            "test:ai-admin-audience",
            "test-local-key-1",
            _signer.ExportSubjectPublicKeyInfoPem());
        public void Dispose() => _signer.Dispose();

        private static string Enc(byte[] value) =>
            Convert.ToBase64String(value).TrimEnd('=')
                .Replace('+','-').Replace('/','_');

        public string Token(
            Action<Dictionary<string, object>>? editClaims = null,
            Action<Dictionary<string, object>>? editHeader = null)
        {
            var header = new Dictionary<string, object>
            {
                ["alg"]="RS256",["kid"]=Trust.KeyId,["typ"]="JWT"
            };
            var claims = new Dictionary<string, object>
            {
                ["iss"]=Trust.Issuer,["aud"]=Trust.Audience,
                ["sub"]="test:admin-subject",["jti"]="test:nonce",
                ["iat"]=Now.ToUnixTimeSeconds()-100,
                ["nbf"]=Now.ToUnixTimeSeconds()-100,
                ["exp"]=Now.ToUnixTimeSeconds()+100
            };
            editClaims?.Invoke(claims);
            editHeader?.Invoke(header);
            return FromJson(JsonSerializer.Serialize(header),
                JsonSerializer.Serialize(claims));
        }

        public string FromJson(string header, string payload)
        {
            var head = Enc(Encoding.UTF8.GetBytes(header));
            var body = Enc(Encoding.UTF8.GetBytes(payload));
            var toSign=Encoding.ASCII.GetBytes(head+"."+body);
            var sig=_signer.SignData(toSign,
                HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
            return head+"."+body+"."+Enc(sig);
        }
    }

    [Fact]
    public void ValidPinnedSignatureIsOnlyTechnicalEvidence()
    {
        using var f=new Fixture();
        var v=AiAdminIdentityBoundary.VerifyEvidence(f.Token(),f.Trust,Now);
        Assert.True(v.CryptographicIdentityEvidenceVerified);
        Assert.Equal("PINNED_SIGNATURE_EVIDENCE_ONLY",v.Status);
        Assert.False(v.RealOperatorAuthenticated);
        Assert.False(v.AdminAuthorized);
        Assert.False(v.CommitteeApproved);
        Assert.False(v.ProductionActivationAuthorized);
        Assert.False(v.ProductionAllowed);
        Assert.Equal(64,v.TokenSha256?.Length);
    }

    [Fact]
    public void NoConfigurationNeverAuthenticates()
    {
        using var f=new Fixture();
        var token=f.Token();
        Assert.False(AiAdminIdentityBoundary.VerifyEvidence(token,null,Now)
            .CryptographicIdentityEvidenceVerified);
        Assert.False(AiAdminIdentityBoundary.VerifyEvidence(token,
            f.Trust with {Issuer=""},Now).CryptographicIdentityEvidenceVerified);
        Assert.False(AiAdminIdentityBoundary.VerifyEvidence(token,
            f.Trust with {RsaPublicKeyPem=""},Now).CryptographicIdentityEvidenceVerified);
    }

    [Theory]
    [InlineData(AiControlPlaneOperation.ReadModelInventory)]
    [InlineData(AiControlPlaneOperation.RegisterModelVersion)]
    [InlineData(AiControlPlaneOperation.RegisterTrainingRequest)]
    [InlineData(AiControlPlaneOperation.ReadEvaluation)]
    [InlineData(AiControlPlaneOperation.RequestCommitteeReview)]
    [InlineData(AiControlPlaneOperation.RecordCommitteeApproval)]
    [InlineData(AiControlPlaneOperation.AuthorizeProductionActivation)]
    [InlineData(AiControlPlaneOperation.RollbackProduction)]
    public void PinnedTokenNeverGrantsAnyControlPlaneOperation(AiControlPlaneOperation op)
    {
        using var f=new Fixture();
        var decision=AiAdminIdentityBoundary.Decide(f.Token(),f.Trust,Now,op);
        Assert.True(decision.CryptographicIdentityEvidenceVerified);
        Assert.False(decision.Allowed);
        Assert.False(decision.AdminAuthenticated);
        Assert.False(decision.HumanReviewCompleted);
        Assert.False(decision.ProductionAllowed);
        Assert.Equal(op,decision.RequestedOperation);
    }

    [Fact]
    public void PrivilegeClaimsCannotReplaceRealGrants()
    {
        using var f=new Fixture();
        var token=f.Token(c=>{
            c["role"]="admin";
            c["committeeApproved"]=true;
            c["modelApproved"]=true;
            c["productionAllowed"]=true;
            c["scope"]="activate:model register:model";
        });
        var result=AiAdminIdentityBoundary.Decide(token,f.Trust,Now,
            AiControlPlaneOperation.AuthorizeProductionActivation);
        Assert.True(result.CryptographicIdentityEvidenceVerified);
        Assert.False(result.Allowed);
        Assert.False(result.ProductionAllowed);
    }

    [Fact]
    public void WrongPinnedIssuerAudienceKeyAndKeyIdFail()
    {
        using var f=new Fixture();
        var token=f.Token();
        using var unrelated=new Fixture();
        var configs=new[] {
            f.Trust with {Issuer="https://other.invalid"},
            f.Trust with {Audience="test:wrong"},
            f.Trust with {KeyId="test:other-key"},
            f.Trust with {RsaPublicKeyPem=unrelated.Trust.RsaPublicKeyPem}
        };
        foreach(var trust in configs)
            Assert.False(AiAdminIdentityBoundary.VerifyEvidence(token,trust,Now)
                .CryptographicIdentityEvidenceVerified);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("HS256")]
    [InlineData("RS512")]
    public void AlgorithmDowngradeFailsEvenWhenSigned(string algorithm)
    {
        using var f=new Fixture();
        var v=AiAdminIdentityBoundary.VerifyEvidence(
            f.Token(editHeader:h=>h["alg"]=algorithm),f.Trust,Now);
        Assert.False(v.CryptographicIdentityEvidenceVerified);
    }

    [Theory]
    [InlineData("jku","https://evil.invalid/jwks")]
    [InlineData("jwk","key")]
    [InlineData("x5u","https://evil.invalid/cert")]
    [InlineData("crit","b64")]
    public void EmbeddedOrExternalKeyHintsCannotOverridePinnedTrust(
        string field,string value)
    {
        using var f=new Fixture();
        var v=AiAdminIdentityBoundary.VerifyEvidence(
            f.Token(editHeader:h=>h[field]=value),f.Trust,Now);
        Assert.False(v.CryptographicIdentityEvidenceVerified);
    }

    [Fact]
    public void ExpiredFutureOrImpossibleTimeClaimsFail()
    {
        using var f=new Fixture();
        foreach(var edit in new Action<Dictionary<string,object>>[] {
            c=>c["exp"]=Now.ToUnixTimeSeconds(),
            c=>c["exp"]=Now.ToUnixTimeSeconds()-1,
            c=>c["nbf"]=Now.ToUnixTimeSeconds()+1,
            c=>c["iat"]=Now.ToUnixTimeSeconds()+1,
            c=>c["iat"]=Now.ToUnixTimeSeconds()+50
        })
            Assert.False(AiAdminIdentityBoundary.VerifyEvidence(
                f.Token(edit),f.Trust,Now).CryptographicIdentityEvidenceVerified);
    }

    [Fact]
    public void MissingOrMalformedRequiredClaimsFail()
    {
        using var f=new Fixture();
        foreach(var field in new[]{"iss","aud","sub","jti","iat","nbf","exp"})
        {
            var token=f.Token(c=>c.Remove(field));
            Assert.False(AiAdminIdentityBoundary.VerifyEvidence(
                token,f.Trust,Now).CryptographicIdentityEvidenceVerified);
        }
        Assert.False(AiAdminIdentityBoundary.VerifyEvidence(
            f.Token(c=>c["aud"]=new[]{"test:ai-admin-audience"}),
            f.Trust,Now).CryptographicIdentityEvidenceVerified);
        Assert.False(AiAdminIdentityBoundary.VerifyEvidence(
            f.Token(c=>c["exp"]="1800000100"),
            f.Trust,Now).CryptographicIdentityEvidenceVerified);
    }

    [Fact]
    public void DuplicateJwtHeaderOrClaimsFailClosed()
    {
        using var f=new Fixture();
        var header="""{"alg":"RS256","typ":"JWT","kid":"test-local-key-1","alg":"none"}""";
        var goodClaims=JsonSerializer.Serialize(new {
            iss=f.Trust.Issuer,aud=f.Trust.Audience,
            sub="test:admin-subject",jti="test:nonce",
            iat=Now.ToUnixTimeSeconds()-1,nbf=Now.ToUnixTimeSeconds()-1,
            exp=Now.ToUnixTimeSeconds()+1});
        Assert.False(AiAdminIdentityBoundary.VerifyEvidence(
            f.FromJson(header,goodClaims),f.Trust,Now)
            .CryptographicIdentityEvidenceVerified);
        var goodHeader="""{"alg":"RS256","typ":"JWT","kid":"test-local-key-1"}""";
        var dupClaims=goodClaims.Insert(1,"\"iss\":\"https://evil.invalid\",");
        Assert.False(AiAdminIdentityBoundary.VerifyEvidence(
            f.FromJson(goodHeader,dupClaims),f.Trust,Now)
            .CryptographicIdentityEvidenceVerified);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    [InlineData("a.b.c")]
    [InlineData("aaa..bbb")]
    [InlineData("eyJhbGciOiJub25lIn0.e30.")]
    public void MalformedTokensRemainDenied(string token)
    {
        using var f=new Fixture();
        Assert.False(AiAdminIdentityBoundary.VerifyEvidence(
            token,f.Trust,Now).CryptographicIdentityEvidenceVerified);
    }

    [Fact]
    public void ModifiedSignaturePayloadNeverVerifies()
    {
        using var f=new Fixture();
        var token=f.Token();
        var parts=token.Split('.');
        parts[1]=parts[1][..^2]+"aa";
        Assert.False(AiAdminIdentityBoundary.VerifyEvidence(
            string.Join(".",parts),f.Trust,Now).CryptographicIdentityEvidenceVerified);
    }

    [Fact]
    public void NoTokenOrUnknownOperationGrantsAnything()
    {
        var result=AiAdminIdentityBoundary.Decide(null,null,Now,
            (AiControlPlaneOperation)999);
        Assert.False(result.Allowed);
        Assert.False(result.CryptographicIdentityEvidenceVerified);
        Assert.False(result.ProductionAllowed);
        Assert.Null(result.TokenSha256);
    }
}
