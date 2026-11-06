namespace Headroom.Client.Api;

// Adds the antiforgery header to every write and remembers the token from any response.
public class AntiforgeryHandler(AntiforgeryToken token) : DelegatingHandler
{
    private static readonly HttpMethod[] SafeMethods = [HttpMethod.Get, HttpMethod.Head, HttpMethod.Options];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (!SafeMethods.Contains(request.Method) && token.Value is { } current)
            request.Headers.TryAddWithoutValidation(AntiforgeryToken.HeaderName, current);

        var response = await base.SendAsync(request, ct);
        if (response.Headers.TryGetValues(AntiforgeryToken.HeaderName, out var values))
            token.Value = values.First();
        return response;
    }
}
