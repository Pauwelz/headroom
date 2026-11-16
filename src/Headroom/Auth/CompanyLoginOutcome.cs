namespace Headroom.Auth;

// The result of a login that failed. It travels as the query value companyLogin.
public static class CompanyLoginOutcome
{
    public const string Provider = "provider";
    public const string Refused = "refused";

    // One message for every refusal: it never says whether an email is known.
    public static string Message(string? code) => code switch
    {
        Provider => "Microsoft refused the login. Ask the Operator to check the Entra ID settings.",
        _ => "Headroom could not log you in. Ask a Planner for help.",
    };
}
