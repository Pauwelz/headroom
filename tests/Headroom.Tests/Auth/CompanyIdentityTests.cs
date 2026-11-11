using Headroom.Auth;
using Headroom.Client.Api;

namespace Headroom.Tests.Auth;

public class CompanyIdentityTests
{
    private static Func<string, string?> Claims(params (string, string)[] claims) =>
        name => claims.Where(c => c.Item1 == name).Select(c => c.Item2).FirstOrDefault();

    [Fact]
    public void EntraUsesTenantAndObjectIdAndTheUserPrincipalName()
    {
        var identity = CompanyIdentity.FromClaims(CompanyLoginKind.Entra, Claims(
            ("tid", "t1"), ("oid", "o1"), ("preferred_username", "ann@company.be"), ("email", "alias@other.be"), ("iss", "x"), ("sub", "s")));
        Assert.Equal(new CompanyIdentity("t1", "o1", "ann@company.be", null), identity);
    }

    [Fact]
    public void EntraNeverUsesTheEmailClaim() =>
        Assert.Null(CompanyIdentity.FromClaims(CompanyLoginKind.Entra, Claims(("tid", "t1"), ("oid", "o1"), ("email", "ann@company.be")))!.Email);

    [Fact]
    public void EntraNeedsTenantAndObjectId() =>
        Assert.Null(CompanyIdentity.FromClaims(CompanyLoginKind.Entra, Claims(("oid", "o1"), ("preferred_username", "a@b.be"))));

    [Fact]
    public void OtherUsesIssuerAndSubjectAndAVerifiedEmail()
    {
        var identity = CompanyIdentity.FromClaims(CompanyLoginKind.Other, Claims(
            ("iss", "https://idp"), ("sub", "s1"), ("email", "ann@company.be"), ("email_verified", "true"), ("name", "Ann")));
        Assert.Equal(new CompanyIdentity("https://idp", "s1", "ann@company.be", "Ann"), identity);
    }

    [Theory]
    [InlineData("false")]
    [InlineData(null)]
    public void OtherIgnoresAnEmailThatIsNotVerified(string? verified)
    {
        var claims = verified is null
            ? Claims(("iss", "i"), ("sub", "s"), ("email", "ann@company.be"))
            : Claims(("iss", "i"), ("sub", "s"), ("email", "ann@company.be"), ("email_verified", verified));
        Assert.Null(CompanyIdentity.FromClaims(CompanyLoginKind.Other, claims)!.Email);
    }

    [Theory]
    [InlineData("https://login.microsoftonline.com/11111111-1111-1111-1111-111111111111/v2.0", true)]
    [InlineData("https://login.microsoftonline.com/contoso.onmicrosoft.com/v2.0", true)]
    [InlineData("https://login.microsoftonline.com/common/v2.0", false)]
    [InlineData("https://login.microsoftonline.com/Organizations/v2.0", false)]
    [InlineData("https://login.microsoftonline.com/consumers", false)]
    [InlineData("https://login.microsoftonline.com/", false)]
    [InlineData("not a url", false)]
    [InlineData("http://login.microsoftonline.com/t/v2.0", false)]
    public void EntraNeedsAnIssuerOfOneTenant(string issuer, bool valid) =>
        Assert.Equal(valid, CompanyIdentity.ValidateIssuer(CompanyLoginKind.Entra, issuer) is null);

    [Theory]
    [InlineData("https://idp.example.com", true)]
    [InlineData("http://localhost:8080/default", true)]
    [InlineData("http://idp.example.com", false)]
    [InlineData("idp.example.com", false)]
    public void OtherNeedsHttpsOrLocalhost(string issuer, bool valid) =>
        Assert.Equal(valid, CompanyIdentity.ValidateIssuer(CompanyLoginKind.Other, issuer) is null);
}
