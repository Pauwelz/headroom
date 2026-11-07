namespace Headroom.Endpoints;

// HTML of the consent screen (ADR 0008). It shows on every authorize request. The form posts the request back
// to /connect/authorize, with the Person's decision. Every Person can tick "Can change".
public static class ConsentPages
{
    private static readonly string[] Steps = ["Log in", "Allow access"];

    public static string Consent(string token, string company, string appName, string personName, IEnumerable<KeyValuePair<string, string?>> request)
    {
        var app = FirstRunPages.E(appName);
        return FirstRunPages.Layout($"Connect {app}", null, Steps, 1, company, $$"""
            <p><strong>{{app}}</strong> wants to connect to Headroom as <strong>{{FirstRunPages.E(personName)}}</strong>.</p>
            <p class="hint">The app can read the planning that you can see.</p>
            <form method="post" action="/connect/authorize">
              {{FirstRunPages.Hidden("__RequestVerificationToken", token)}}
              <label><input type="checkbox" name="canChange"> Can change: the app can also change what you can change.</label>
              {{string.Concat(request.Select(p => FirstRunPages.Hidden(p.Key, p.Value ?? "")))}}
              <button name="decision" value="allow">Allow</button>
              <button name="decision" value="deny">Cancel</button>
            </form>
            """);
    }
}
