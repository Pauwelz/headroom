namespace Headroom.Client.Shell;

/// <summary>The one search of every autocomplete: a case-insensitive "contains" match. Empty text shows all items.</summary>
public static class AutocompleteSearch
{
    public static IEnumerable<T> Match<T>(IEnumerable<T> items, string? text, Func<T, string> label) =>
        string.IsNullOrWhiteSpace(text)
            ? items
            : items.Where(item => label(item).Contains(text.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The function a MudAutocomplete takes as SearchFunc.</summary>
    public static Func<string?, CancellationToken, Task<IEnumerable<T>>> For<T>(IEnumerable<T> items, Func<T, string> label) =>
        (text, _) => Task.FromResult(Match(items, text, label));
}
