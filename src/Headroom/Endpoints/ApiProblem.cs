namespace Headroom.Endpoints;

// One way to refuse a request. The client shows the detail text to the user.
public static class ApiProblem
{
    public static IResult Problem(int status, string detail) => Results.Problem(detail: detail, statusCode: status);

    public static IResult Problem(Refusal refusal) => Problem(refusal.Status, refusal.Detail);

    public const string NameDetail = "Enter a name of 1 to 200 characters.";

    // The one name rule of Access tokens and Webhooks: trimmed, required, at most 200 characters.
    public static string? CleanName(string? name) => name?.Trim() is { Length: > 0 and <= 200 } clean ? clean : null;
}
