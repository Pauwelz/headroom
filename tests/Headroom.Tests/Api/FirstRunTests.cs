using System.Net;
using System.Text.RegularExpressions;
using Headroom.Auth;
using Headroom.Data;
using Headroom.Email;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Headroom.Tests.Api;

public class FakeEmailSender : IEmailSender
{
    public List<(string To, string Company, string Code)> Sent { get; } = [];

    public Task SendLoginCodeAsync(string toAddress, string companyName, string code, CancellationToken ct)
    {
        Sent.Add((toAddress, companyName, code));
        return Task.CompletedTask;
    }
}

public class FirstRunTests : IClassFixture<ClockedHeadroomApp>
{
    private readonly FakeEmailSender _mail;
    private readonly TestClock _time;
    private readonly WebApplicationFactory<Program> _factory;

    public FirstRunTests(ClockedHeadroomApp app)
    {
        _factory = app.Factory;
        _mail = app.Mail;
        _time = app.Time;
        _time.Set(ClockedHeadroomApp.Start);
    }

    [Fact]
    public async Task CodeIsSingleUseAndLimitedToFiveAttempts()
    {
        using var scope = _factory.Services.CreateScope();
        var codes = scope.ServiceProvider.GetRequiredService<LoginCodeService>();

        Assert.True(await codes.IssueAsync("single@example.com", "Acme", TestContext.Current.CancellationToken));
        var code = _mail.Sent.Last(m => m.To == "single@example.com").Code;
        Assert.Matches(@"^\d{6}$", code);
        Assert.True(await codes.VerifyAsync("single@example.com", code, TestContext.Current.CancellationToken));
        Assert.False(await codes.VerifyAsync("single@example.com", code, TestContext.Current.CancellationToken));

        Assert.True(await codes.IssueAsync("attempts@example.com", "Acme", TestContext.Current.CancellationToken));
        var good = _mail.Sent.Last(m => m.To == "attempts@example.com").Code;
        var bad = good == "000000" ? "000001" : "000000";
        for (var i = 0; i < 5; i++) Assert.False(await codes.VerifyAsync("attempts@example.com", bad, TestContext.Current.CancellationToken));
        Assert.False(await codes.VerifyAsync("attempts@example.com", good, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CodeVerifiesWhenTheEmailCaseDiffers()
    {
        using var scope = _factory.Services.CreateScope();
        var codes = scope.ServiceProvider.GetRequiredService<LoginCodeService>();

        Assert.True(await codes.IssueAsync("Mixed.Case@example.com", "Acme", TestContext.Current.CancellationToken));
        var code = _mail.Sent.Last(m => m.To == "Mixed.Case@example.com").Code;

        Assert.True(await codes.VerifyAsync("mixed.case@EXAMPLE.com", code, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CodeExpiresAfterTenMinutesAndIsLimitedToThreePerFifteenMinutes()
    {
        using var scope = _factory.Services.CreateScope();
        var codes = scope.ServiceProvider.GetRequiredService<LoginCodeService>();
        const string email = "limit@example.com";

        Assert.True(await codes.IssueAsync(email, "Acme", TestContext.Current.CancellationToken));
        var first = _mail.Sent.Last(m => m.To == email).Code;
        _time.Advance(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1));
        Assert.False(await codes.VerifyAsync(email, first, TestContext.Current.CancellationToken));

        Assert.True(await codes.IssueAsync(email, "Acme", TestContext.Current.CancellationToken));
        Assert.True(await codes.IssueAsync(email, "Acme", TestContext.Current.CancellationToken));
        var sentBefore = _mail.Sent.Count;
        Assert.False(await codes.IssueAsync(email, "Acme", TestContext.Current.CancellationToken));
        Assert.Equal(sentBefore, _mail.Sent.Count);

        _time.Advance(TimeSpan.FromMinutes(16));
        Assert.True(await codes.IssueAsync(email, "Acme", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UsingACodeDoesNotFreeASlotInTheRateLimit()
    {
        using var scope = _factory.Services.CreateScope();
        var codes = scope.ServiceProvider.GetRequiredService<LoginCodeService>();
        const string email = "used@example.com";

        for (var i = 0; i < 3; i++)
        {
            Assert.True(await codes.IssueAsync(email, "Acme", TestContext.Current.CancellationToken));
            _time.Advance(TimeSpan.FromSeconds(1)); // the newest code is the one with the latest CreatedAt
        }
        Assert.True(await codes.VerifyAsync(email, _mail.Sent.Last(m => m.To == email).Code, TestContext.Current.CancellationToken));

        Assert.False(await codes.IssueAsync(email, "Acme", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnExpiredOrUsedCodeLosesItsHashAtOnceAndTheRowLeavesAfterTheWindow()
    {
        using var scope = _factory.Services.CreateScope();
        var codes = scope.ServiceProvider.GetRequiredService<LoginCodeService>();
        var db = scope.ServiceProvider.GetRequiredService<HeadroomDbContext>();
        const string email = "sweep@example.com";
        Assert.True(await codes.IssueAsync(email, "Acme", TestContext.Current.CancellationToken));
        _time.Advance(TimeSpan.FromSeconds(1)); // the newest code is the one with the latest CreatedAt
        Assert.True(await codes.IssueAsync(email, "Acme", TestContext.Current.CancellationToken));
        Assert.True(await codes.VerifyAsync(email, _mail.Sent.Last(m => m.To == email).Code, TestContext.Current.CancellationToken));
        Assert.Equal(2, db.LoginCodes.Count(c => c.Email == email));
        Assert.Single(WithHash(db, email));

        _time.Advance(TimeSpan.FromMinutes(11));
        await codes.IssueAsync("other-sweep@example.com", "Acme", TestContext.Current.CancellationToken);

        Assert.Empty(WithHash(db, email));
        Assert.Equal(2, db.LoginCodes.Count(c => c.Email == email));

        _time.Advance(TimeSpan.FromMinutes(5));
        await codes.IssueAsync("other-sweep@example.com", "Acme", TestContext.Current.CancellationToken);

        Assert.Equal(0, db.LoginCodes.Count(c => c.Email == email));
    }

    [Fact]
    public async Task FirstRunCreatesTheInstanceAndThenRefusesEveryRequest()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var form = await client.GetAsync("/first-run", TestContext.Current.CancellationToken);
        await PagePolicy.AssertAsync(form);
        var formHtml = await form.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var token = Token(formHtml);
        var fields = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["company"] = "Acme",
            ["name"] = "Ann Planner",
            ["email"] = "ann@example.com",
        };

        // An invalid email sends no code.
        var invalid = await client.PostAsync("/first-run", new FormUrlEncodedContent(
            new Dictionary<string, string>(fields) { ["email"] = "not-an-email" }), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.DoesNotContain(_mail.Sent, m => m.To == "not-an-email");

        var sent = await client.PostAsync("/first-run", new FormUrlEncodedContent(fields), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        var (to, company, code) = _mail.Sent.Single(m => m.To == "ann@example.com");
        Assert.Equal("Acme", company);
        token = Token(await sent.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        fields["__RequestVerificationToken"] = token;

        // A wrong code creates nothing.
        var wrong = await client.PostAsync("/first-run/verify", new FormUrlEncodedContent(
            new Dictionary<string, string>(fields) { ["code"] = code == "000000" ? "000001" : "000000" }), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Empty(Query(db => db.People.ToList()));

        var done = await client.PostAsync("/first-run/verify", new FormUrlEncodedContent(
            new Dictionary<string, string>(fields) { ["code"] = code }), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, done.StatusCode);
        Assert.Contains(done.Headers.GetValues("Set-Cookie"), c => c.StartsWith("headroom.auth="));
        Assert.Contains(done.Headers.GetValues("Set-Cookie"), c => c.StartsWith("headroom-passkey-offer=1", StringComparison.Ordinal));

        var person = Assert.Single(Query(db => db.People.ToList()));
        Assert.Equal(("Ann Planner", "ann@example.com", true), (person.Name, person.Email, person.IsPlanner));
        Assert.Equal(person.Id, person.LastChangedBy);
        Assert.Equal(7, person.Id.Version);
        var settings = Assert.Single(Query(db => db.InstanceSettings.ToList()));
        Assert.Equal("Acme", settings.CompanyName);
        Assert.Equal(7, settings.Id.Version);
        Assert.Equal(person.Id, settings.LastChangedBy);
        Assert.Equal(_time.GetUtcNow(), settings.DateModified);
        var contract = Assert.Single(Query(db => db.Contracts.ToList()));
        Assert.Equal(new DateOnly(2026, 11, 2), contract.StartDate);
        Assert.Equal(person.Id, contract.PersonId);
        Assert.Equal(person.Id, contract.LastChangedBy);
        Assert.Equal([8m, 8, 8, 8, 8, 0, 0], new[]
        {
            contract.HoursMonday, contract.HoursTuesday, contract.HoursWednesday, contract.HoursThursday,
            contract.HoursFriday, contract.HoursSaturday, contract.HoursSunday,
        });
        Assert.Single(Query(db => db.Sessions.ToList()));

        // The screen is closed for good.
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/first-run", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/first-run", new FormUrlEncodedContent(fields), TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/first-run/verify", new FormUrlEncodedContent(fields), TestContext.Current.CancellationToken)).StatusCode);
        Assert.Single(Query(db => db.People.ToList()));

        // The screen stays closed when every Planner is gone: the Instance settings row decides.
        Query(db =>
        {
            db.Sessions.ExecuteDelete();
            db.Contracts.ExecuteDelete();
            return db.People.ExecuteDelete();
        });
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/first-run", TestContext.Current.CancellationToken)).StatusCode);
    }

    private static List<LoginCode> WithHash(HeadroomDbContext db, string email) =>
        db.LoginCodes.AsNoTracking().Where(c => c.Email == email).ToList().Where(c => c.CodeHash.Length > 0).ToList();

    private T Query<T>(Func<HeadroomDbContext, T> query)
    {
        using var scope = _factory.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<HeadroomDbContext>());
    }

    private static string Token(string html) =>
        Regex.Match(html, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"").Groups[1].Value;
}
