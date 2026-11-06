using System.Net;
using System.Text;
using Headroom.Client.Api;
using Headroom.Client.Help;
using Headroom.Client.People;

namespace Headroom.Tests.Shell;

public class ApiCallTests
{
    private sealed class Reply(HttpStatusCode status, string body, string contentType) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, contentType) });
    }

    private static HttpClient Client(HttpStatusCode status, string body, string contentType = "application/problem+json") =>
        new(new Reply(status, body, contentType)) { BaseAddress = new Uri("https://localhost") };

    [Fact]
    public async Task AProblemShowsItsDetail()
    {
        var text = await ApiCall.SendAsync(Client(HttpStatusCode.Conflict, """{"detail":"Taken."}""").DeleteAsync("api/x", TestContext.Current.CancellationToken));

        Assert.Equal("Taken.", text);
    }

    [Theory]
    [InlineData("", "application/problem+json")]
    [InlineData("<html>Bad gateway</html>", "text/html")]
    [InlineData("not json", "application/problem+json")]
    [InlineData("{}", "application/problem+json")]
    public async Task AnEmptyOrNonJsonErrorBodyGivesAGenericText(string body, string contentType)
    {
        var text = await ApiCall.SendAsync(Client(HttpStatusCode.BadGateway, body, contentType).DeleteAsync("api/x", TestContext.Current.CancellationToken));

        Assert.Equal("The server refused the request (502).", text);
    }

    [Fact]
    public async Task APlannerOnlyEndpointGivesTheSameTextForEveryClient()
    {
        var client = Client(HttpStatusCode.Forbidden, "");

        var plain = await ApiCall.SendAsync(client.DeleteAsync("api/x", TestContext.Current.CancellationToken));
        var people = await Assert.ThrowsAsync<ApiException>(() => new PeopleApi(client).DeleteAsync(Guid.NewGuid()));

        Assert.Equal(plain, people.Message);
    }

    [Theory]
    [InlineData("", "application/problem+json")]
    [InlineData("<html>Bad gateway</html>", "text/html")]
    public async Task PeopleApiDoesNotCrashOnAnEmptyOrNonJsonErrorBody(string body, string contentType)
    {
        var api = new PeopleApi(Client(HttpStatusCode.InternalServerError, body, contentType));

        var error = await Assert.ThrowsAsync<ApiException>(() => api.DeleteAsync(Guid.NewGuid()));

        Assert.Equal("The server refused the request (500).", error.Message);
    }

    [Fact]
    public async Task HelpApiThrowsTheErrorTextForAServerError()
    {
        var api = new HelpApi(Client(HttpStatusCode.InternalServerError, ""));

        var error = await Assert.ThrowsAsync<ApiException>(() => api.TopicAsync("people"));

        Assert.Equal("The server refused the request (500).", error.Message);
    }
}
