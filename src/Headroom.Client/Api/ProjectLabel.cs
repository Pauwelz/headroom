namespace Headroom.Client.Api;

/// <summary>The label of a Project next to its Client, as users see it.</summary>
public static class ProjectLabel
{
    public static string Format(string clientName, string projectName) => $"{clientName} / {projectName}";
}
