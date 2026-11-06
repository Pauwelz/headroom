using Headroom.Client.Api;
using Headroom.Client.People;

namespace Headroom.Tests.People;

public class EmploymentTextTests
{
    private static PersonResponse Person(string status, DateOnly? end = null) =>
        new("Person", Guid.NewGuid(), "Zed", "zed@example.com", false, end, null, status, true, 40,
            new PersonLocks(null, null, null, null, null), [], null, default);

    [Fact]
    public void LeavingNamesTheLastWorkingDayAndTheDayTheLoginStops() =>
        Assert.Equal("Leaving. The last working day is 13/11/2026. The login stops on 14/11/2026.",
            EmploymentText.Sentence(Person(PersonStatus.Leaving, new(2026, 11, 13))));

    [Fact]
    public void ANullReferenceShowsAsSystem() =>
        Assert.Equal("System", EmploymentText.ChangedBy(null, []));

    [Fact]
    public void ChangedByShowsTheNameOfThePerson()
    {
        var zed = Person(PersonStatus.Active);
        Assert.Equal("Zed", EmploymentText.ChangedBy(zed.Id, [zed]));
    }
}
