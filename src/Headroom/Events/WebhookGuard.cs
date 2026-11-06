using Headroom.Client.Shell;
using Microsoft.Security.AntiSSRF;

namespace Headroom.Events;

// Lets a Webhook reach only public addresses (SSRF). The URL check runs when a Planner saves the Webhook.
// Microsoft AntiSSRF checks the address at connect time, so a DNS name that later points to a non-public address is blocked too.
public static class WebhookGuard
{
    public static string? UrlProblem(string? raw, bool development, out Uri url) => UrlRules.WebhookUrl(raw, development, out url);

    // No redirects, so a receiver cannot send Headroom to another address. allowInternal is for Development.
    // No proxy: AntiSSRF checks only the proxy address, so a system proxy would bypass the guard.
    // AntiSSRFHandler always sets UseProxy = false on its inner handler and does not expose the setting.
    public static HttpMessageHandler CreateHandler(bool allowInternal)
    {
        if (allowInternal) return new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false };
        var handler = new AntiSSRFPolicy(PolicyConfigOptions.ExternalOnlyLatest).GetHandler();
        handler.AllowAutoRedirect = false;
        return new RefusalAsHttpError { InnerHandler = handler };
    }

    // EventSender records an HttpRequestException as a failed delivery. AntiSSRFException derives only from Exception.
    private sealed class RefusalAsHttpError : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            try { return await base.SendAsync(request, ct); }
            catch (AntiSSRFException e) { throw new HttpRequestException(e.Message, e); }
        }
    }
}
