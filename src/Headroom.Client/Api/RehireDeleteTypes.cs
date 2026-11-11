namespace Headroom.Client.Api;

/// <summary>Rehire a Person who has left. Hours start on the return date.</summary>
public record RehireRequest(DateOnly ReturnDate, WeekHours Hours);

/// <summary>What a Delete removes, for the dialog. Reason is null when the Person can be deleted. Otherwise it is the text for the disabled button.</summary>
public record DeleteCheck(int Contracts, int Allocations, int TimeOff, string? Reason);
