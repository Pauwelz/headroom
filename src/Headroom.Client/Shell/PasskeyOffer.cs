namespace Headroom.Client.Shell;

// The offer to save a Passkey after a Login code login. The server sets the cookie, the client takes and deletes it.
public static class PasskeyOffer
{
    public const string CookieName = "headroom-passkey-offer";

    public static bool ShouldShow(bool marked, bool supported) => marked && supported;
}
