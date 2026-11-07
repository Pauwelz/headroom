using Microsoft.JSInterop;

namespace Headroom.Client.AccessTokens;

// The one registration ceremony: the Account page button and the offer after a Login code login both call it.
public class PasskeyRegistration(PasskeyApi api, IJSRuntime js) : IAsyncDisposable
{
    private IJSObjectReference? _module;

    private async Task<IJSObjectReference> ModuleAsync() =>
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./AccessTokens/PasskeySection.razor.js");

    public async Task<bool> IsSupportedAsync() => await (await ModuleAsync()).InvokeAsync<bool>("isSupported");

    public async Task<bool> TakeOfferMarkerAsync(string cookieName) =>
        await (await ModuleAsync()).InvokeAsync<bool>("takeOfferMarker", cookieName);

    // Saved is true when the Passkey is stored. Error is the text to show. Both are empty when the Person closes the browser prompt.
    public async Task<(bool Saved, string? Error)> SaveAsync()
    {
        try
        {
            var credential = await (await ModuleAsync()).InvokeAsync<string?>("create", await api.OptionsAsync());
            if (credential is null) return (false, null);
            var error = await api.SaveAsync(credential);
            return (error is null, error);
        }
        catch (JSException)
        {
            return (false, "Your browser could not create a Passkey.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is not null) await _module.DisposeAsync();
    }
}
