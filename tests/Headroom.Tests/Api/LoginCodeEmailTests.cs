using Headroom.Auth;
using Headroom.Email;
using Headroom.Tests.Architecture;
using MimeKit;

namespace Headroom.Tests.Api;

public class LoginCodeEmailTests
{
    private static string Part(string company, string code, string subtype) =>
        ((Multipart)LoginCodeEmail.Body(company, code)).OfType<TextPart>().Single(p => p.ContentType.MediaSubtype == subtype).Text;

    [Theory]
    [InlineData("plain")]
    [InlineData("html")]
    public void EveryPartHasTheCodeAndTheDuration(string subtype)
    {
        var text = Part("Acme", "482913", subtype);
        Assert.Contains("482913", text);
        Assert.Contains(LoginCodeService.ValidityText, text);
    }

    [Fact]
    public void SubjectHasNoCode() => Assert.DoesNotContain("482913", LoginCodeEmail.Subject);

    [Fact]
    public void HtmlEncodesTheCompanyAndLoadsNothing()
    {
        var html = Part("<b>A&B</b>", "482913", "html");
        Assert.Contains("&lt;b&gt;A&amp;B&lt;/b&gt;", html);
        Assert.DoesNotContain("<b>", html);
        Assert.DoesNotContain("<img", html);
        Assert.DoesNotContain("<svg", html);
        Assert.DoesNotContain("http", html);
    }

    // brand.css and the email cannot share a constant, so this test keeps the amber equal.
    [Fact]
    public void AmberMatchesBrandCss() =>
        Assert.Contains($"--hr-amber: {LoginCodeEmail.Amber};", File.ReadAllText(Path.Combine(Repo.Root, "src/Headroom/wwwroot/brand.css")));

    // The user guide states the code duration in prose, so this test keeps it equal to the code.
    [Fact]
    public void UserGuideStatesTheDuration() =>
        Assert.Contains($"The code works for {LoginCodeService.ValidityText}", File.ReadAllText(Path.Combine(Repo.Root, "docs/guides/user-guide/getting-started.md")));
}
