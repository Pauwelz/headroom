using System.Text.RegularExpressions;

namespace Headroom.Tests.Architecture;

internal static class Repo
{
    public static readonly string Root = FindRoot();

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Headroom.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Headroom.slnx not found above the test assembly.");
    }

    /// <summary>Source lines (file relative to the repo root with '/', 1-based line, text). Comment lines are skipped.</summary>
    public static IEnumerable<(string File, int Line, string Text)> Lines(string projectDir, params string[] extensions)
    {
        var src = Path.Combine(Root, "src", projectDir);
        var files = Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories)
            .Where(f => extensions.Contains(Path.GetExtension(f)))
            .Where(f => !Regex.IsMatch(f, @"[/\\](obj|bin)[/\\]"));
        foreach (var f in files)
        {
            var rel = Path.GetRelativePath(Root, f).Replace('\\', '/');
            var lines = File.ReadAllLines(f);
            for (var i = 0; i < lines.Length; i++)
            {
                var t = lines[i].TrimStart();
                if (t.StartsWith("//") || t.StartsWith("*") || t.StartsWith("/*")) continue;
                yield return (rel, i + 1, lines[i]);
            }
        }
    }
}
