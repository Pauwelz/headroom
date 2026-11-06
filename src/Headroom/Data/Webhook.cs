namespace Headroom.Data;

// A URL that receives every Event. Secret is the whsec_ signing key.
// shortcut: the secret is stored as plain text, because signing needs it. Encrypt it with Data Protection when a database backup leaves the company's subscription.
public class Webhook : Record
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string Secret { get; set; } = "";
}
