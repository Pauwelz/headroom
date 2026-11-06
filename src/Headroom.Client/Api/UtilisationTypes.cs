namespace Headroom.Client.Api;

// Shared API types of GET /api/utilisation. Field names are a contract.
// The server computes all hours. The client only renders them and picks the colour band.

/// <summary>Hours of one Project in a cell. Status is "tentative", "confirmed" or "archived". The names are for display.</summary>
public record UtilisationProject(Guid ProjectId, Guid ClientId, string ClientName, string ProjectName, string Status, decimal Hours)
{
    public string Type => "UtilisationProject";
}

/// <summary>
/// Hours of one Person (or of all People) in one period.
/// Capacity = ContractHours - PublicHolidayHours - CompanyDayOffHours - TimeOffHours.
/// Utilisation = (Confirmed + Tentative + Archived) / Capacity, null when Capacity is 0.
/// OverDays counts the over-allocated days (person-days for the "All People" row).
/// </summary>
public record UtilisationCell(
    decimal ContractHours, decimal PublicHolidayHours, decimal CompanyDayOffHours, decimal TimeOffHours, decimal Capacity,
    decimal Confirmed, decimal Tentative, decimal Archived, int OverDays, decimal? Utilisation,
    IReadOnlyList<UtilisationProject> Projects)
{
    public string Type => "UtilisationCell";
}

public record UtilisationPeriod(DateOnly StartDate, DateOnly EndDate)
{
    public string Type => "UtilisationPeriod";
}

/// <summary>Cells has one cell per period. Total covers the whole range. PersonId is null for "All People".</summary>
public record UtilisationRow(Guid? PersonId, string Name, IReadOnlyList<UtilisationCell> Cells, UtilisationCell Total)
{
    public string Type => "UtilisationRow";
}

/// <summary>Period is "week" or "month". StartDate and EndDate are the inclusive range. People are sorted by name.</summary>
public record UtilisationResponse(
    string Type, DateOnly StartDate, DateOnly EndDate, string Period,
    IReadOnlyList<UtilisationPeriod> Periods, UtilisationRow All, IReadOnlyList<UtilisationRow> People);
