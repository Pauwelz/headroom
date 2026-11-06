using System.Reflection;
using Microsoft.AspNetCore.StaticFiles;
using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Headroom.Endpoints;

// In-app help. The user guide files are embedded at build time, so the help matches the deployed version.
// Every logged-in Person may read it. Names are looked up in the embedded set only, so a path cannot reach other files.
public static class HelpEndpoints
{
    private const string Prefix = "help/";

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
        .DisableHtml() // The client shows the result as raw markup, so the guide must not inject HTML.
        .Build();

    private static readonly Assembly Source = typeof(HelpEndpoints).Assembly;

    /// <summary>Renders Markdown. A link to <c>x.md</c> becomes <c>/help/x</c>. An image <c>images/x.png</c> becomes <c>/api/help/images/x.png</c>.</summary>
    public static string Render(string markdown)
    {
        var document = Markdown.Parse(markdown, Pipeline);
        foreach (var link in document.Descendants<LinkInline>())
        {
            if (link.Url is not { } url) continue;
            if (link.IsImage && url.StartsWith("images/")) link.Url = "/api/help/" + url;
            else if (!link.IsImage && !url.Contains("://"))
            {
                var anchor = url.IndexOf('#');
                var path = anchor < 0 ? url : url[..anchor];
                if (path.EndsWith(".md")) link.Url = "/help/" + path[..^3] + (anchor < 0 ? "" : url[anchor..]);
            }
        }
        return document.ToHtml(Pipeline);
    }

    public static void MapHelpEndpoints(this RouteGroupBuilder api)
    {
        var help = api.MapGroup("/help").RequireAuthorization();
        help.MapGet("/images/{name}", (string name) =>
            IsFileName(name) && Source.GetManifestResourceStream($"{Prefix}images/{name}") is { } stream
                ? Results.File(stream, ContentType(name))
                : Results.NotFound());
        help.MapGet("/{topic}", (string topic, HttpResponse response) =>
        {
            if (!IsFileName(topic) || Source.GetManifestResourceStream($"{Prefix}{topic}.md") is not { } stream)
                return Results.NotFound();
            using var reader = new StreamReader(stream);
            // The client fetches this fragment. The policy blocks script and framing if a browser opens the URL directly.
            response.Headers.ContentSecurityPolicy = "default-src 'none'; img-src 'self'; frame-ancestors 'none'";
            return Results.Content(Render(reader.ReadToEnd()), "text/html");
        });
    }

    private static bool IsFileName(string name) => name.IndexOfAny(['/', '\\']) < 0 && !name.Contains("..");

    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    private static string ContentType(string name) =>
        ContentTypes.TryGetContentType(name, out var type) ? type : "application/octet-stream";
}
