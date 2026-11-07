using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Headroom.Tests.Api;

// A HeadroomApp whose clock tests control. Each test sets the time it needs first: Time.Set(Start).
public sealed class ClockedHeadroomApp : HeadroomApp
{
    // Monday 2 November 2026 in Brussels.
    public static readonly DateTimeOffset Start = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    public TestClock Time { get; } = new(Start);

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureServices(services => services.AddSingleton<TimeProvider>(Time));
}

// Unlike FakeTimeProvider, it can go back, so tests in one class can each start at a known time.
public sealed class TestClock(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc; // the same as FakeTimeProvider

    public void Set(DateTimeOffset now) => _now = now;

    public void Advance(TimeSpan by) => _now += by;
}
