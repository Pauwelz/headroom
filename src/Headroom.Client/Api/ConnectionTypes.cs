namespace Headroom.Client.Api;

// A Connection (GLOSSARY): one Person and one Connected app. LastUsed is the newest token of the Connection.
// CanChange is true when the Person is a Planner and allowed the Connection to change things.
public record ConnectionResponse(string Id, string ConnectedApp, string Owner, DateTimeOffset DateCreated, DateTimeOffset? LastUsed, bool CanChange)
{
    public string Type => "Connection";
}
