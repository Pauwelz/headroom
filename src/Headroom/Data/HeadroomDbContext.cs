using Headroom.Events;
using Headroom.Telemetry;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using OpenIddict.EntityFrameworkCore;

namespace Headroom.Data;

public class HeadroomDbContext(DbContextOptions<HeadroomDbContext> options, TimeProvider time)
    : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<InstanceSettings> InstanceSettings => Set<InstanceSettings>();
    public DbSet<Person> People => Set<Person>();
    public DbSet<Contract> Contracts => Set<Contract>();
    public DbSet<TimeOff> TimeOff => Set<TimeOff>();
    public DbSet<CompanyDayOff> CompanyDaysOff => Set<CompanyDayOff>();
    public DbSet<AccessToken> AccessTokens => Set<AccessToken>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Allocation> Allocations => Set<Allocation>();
    public DbSet<LoginCode> LoginCodes => Set<LoginCode>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
    public DbSet<Webhook> Webhooks => Set<Webhook>();
    public DbSet<Event> Events => Set<Event>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.UseOpenIddict();
        // PostgreSQL compares case-sensitively. Email, Client name and Project name must match without case. ICU level 2 ignores case only.
        // A nondeterministic collation rejects LIKE, so never run Contains or StartsWith on these columns in the database.
        modelBuilder.HasCollation("case_insensitive", locale: "und-u-ks-level2", provider: "icu", deterministic: false);
        // The server generates ids, so the database does not.
        modelBuilder.Entity<InstanceSettings>().Property(s => s.Id).ValueGeneratedNever();
        modelBuilder.Entity<Person>(person =>
        {
            person.Property(p => p.Id).ValueGeneratedNever();
            person.Property(p => p.Name).HasMaxLength(200);
            person.Property(p => p.Email).HasMaxLength(254).UseCollation("case_insensitive");
            // A unique index allows many nulls, so erased Persons (null email) do not clash.
            person.HasIndex(p => p.Email).IsUnique();
        });
        modelBuilder.Entity<Contract>(contract =>
        {
            contract.Property(c => c.Id).ValueGeneratedNever();
            contract.HasOne<Person>().WithMany().HasForeignKey(c => c.PersonId);
            contract.Property(c => c.HoursMonday).HasPrecision(4, 2);
            contract.Property(c => c.HoursTuesday).HasPrecision(4, 2);
            contract.Property(c => c.HoursWednesday).HasPrecision(4, 2);
            contract.Property(c => c.HoursThursday).HasPrecision(4, 2);
            contract.Property(c => c.HoursFriday).HasPrecision(4, 2);
            contract.Property(c => c.HoursSaturday).HasPrecision(4, 2);
            contract.Property(c => c.HoursSunday).HasPrecision(4, 2);
        });
        modelBuilder.Entity<TimeOff>(timeOff =>
        {
            timeOff.Property(t => t.Id).ValueGeneratedNever();
            timeOff.HasOne<Person>().WithMany().HasForeignKey(t => t.PersonId);
            timeOff.Property(t => t.StartPart).HasConversion<string>().HasMaxLength(20);
            timeOff.Property(t => t.EndPart).HasConversion<string>().HasMaxLength(20);
            timeOff.Ignore(t => t.Term);
            timeOff.HasIndex(t => new { t.PersonId, t.StartDate });
        });
        modelBuilder.Entity<CompanyDayOff>(day =>
        {
            day.Property(d => d.Id).ValueGeneratedNever();
            day.Property(d => d.Name).HasMaxLength(200);
            day.HasIndex(d => d.Date).IsUnique();
        });
        modelBuilder.Entity<AccessToken>(token =>
        {
            token.Property(t => t.Id).ValueGeneratedNever();
            token.Property(t => t.Name).HasMaxLength(200);
            token.Property(t => t.TokenHash).HasMaxLength(32).IsFixedLength();
            token.HasIndex(t => t.TokenHash).IsUnique();
            token.HasOne<Person>().WithMany().HasForeignKey(t => t.PersonId);
        });
        modelBuilder.Entity<Client>(client =>
        {
            client.Property(c => c.Id).ValueGeneratedNever();
            client.Property(c => c.Name).HasMaxLength(200).UseCollation("case_insensitive");
            client.HasIndex(c => c.Name).IsUnique();
        });
        modelBuilder.Entity<Project>(project =>
        {
            project.Property(p => p.Id).ValueGeneratedNever();
            project.Property(p => p.Name).HasMaxLength(200).UseCollation("case_insensitive");
            project.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
            project.HasOne<Client>().WithMany().HasForeignKey(p => p.ClientId);
            project.HasIndex(p => new { p.ClientId, p.Name }).IsUnique();
        });
        modelBuilder.Entity<Allocation>(allocation =>
        {
            allocation.Property(a => a.Id).ValueGeneratedNever();
            allocation.HasOne<Person>().WithMany().HasForeignKey(a => a.PersonId);
            allocation.HasOne<Project>().WithMany().HasForeignKey(a => a.ProjectId);
            allocation.HasIndex(a => new { a.PersonId, a.ProjectId });
        });
        modelBuilder.Entity<LoginCode>(code =>
        {
            code.Property(c => c.Id).ValueGeneratedNever();
            code.Property(c => c.Email).HasMaxLength(254).UseCollation("case_insensitive");
            code.HasIndex(c => c.Email);
        });
        modelBuilder.Entity<Session>().Property(s => s.Id).ValueGeneratedNever();
        modelBuilder.Entity<Webhook>(webhook =>
        {
            webhook.Property(w => w.Id).ValueGeneratedNever();
            webhook.Property(w => w.Name).HasMaxLength(200);
            webhook.Property(w => w.Url).HasMaxLength(2000);
            webhook.Property(w => w.Secret).HasMaxLength(100);
        });
        modelBuilder.Entity<Event>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Type).HasMaxLength(100);
            e.Property(x => x.LastError).HasMaxLength(500);
            e.HasOne<Webhook>().WithMany().HasForeignKey(x => x.WebhookId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.WebhookId);
            e.Property(x => x.Attempts).HasDefaultValue(0);
        });
    }

    // shortcut: the sync save blocks on the async Event lookup. Nothing calls it today, so upgrade if a caller appears.
    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        SaveChangesAsync(acceptAllChangesOnSuccess).GetAwaiter().GetResult();

    // Events are stored in the same save, so they share the transaction of the change (ADR 0004).
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampDateModified();
        var changes = EventRecorder.Collect(ChangeTracker.Entries());
        if (changes.Count > 0 && await Webhooks.Select(w => w.Id).ToListAsync(cancellationToken) is { Count: > 0 } webhookIds)
            Events.AddRange(await EventRecorder.BuildAsync(this, changes, webhookIds, time.GetUtcNow(), cancellationToken));
        var saved = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        foreach (var change in changes) HeadroomMetrics.RecordChange(change.Type);
        return saved;
    }

    private void StampDateModified()
    {
        var now = time.GetUtcNow();
        foreach (var entry in ChangeTracker.Entries<Record>()
                     .Where(e => e.State is EntityState.Added or EntityState.Modified))
            entry.Entity.DateModified = now;
    }
}
