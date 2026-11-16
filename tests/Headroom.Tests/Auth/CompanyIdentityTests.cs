using Headroom.Auth;

namespace Headroom.Tests.Auth;

public class CompanyIdentityTests
{
    private static Func<string, string?> Claims(params (string, string)[] claims) =>
        name => claims.Where(c => c.Item1 == name).Select(c => c.Item2).FirstOrDefault();

    [Fact]
    public void UsesTenantAndObjectIdAndTheUserPrincipalName()
    {
        var identity = CompanyIdentity.FromClaims(Claims(
            ("tid", "t1"), ("oid", "o1"), ("preferred_username", "ann@company.be"), ("email", "alias@other.be"), ("iss", "x"), ("sub", "s")));
        Assert.Equal(new CompanyIdentity("t1", "o1", "ann@company.be", null), identity);
    }

    [Fact]
    public void NeverUsesTheEmailClaim() =>
        Assert.Null(CompanyIdentity.FromClaims(Claims(("tid", "t1"), ("oid", "o1"), ("email", "ann@company.be")))!.Email);

    [Fact]
    public void NeedsTenantAndObjectId() =>
        Assert.Null(CompanyIdentity.FromClaims(Claims(("oid", "o1"), ("preferred_username", "a@b.be"))));
}
