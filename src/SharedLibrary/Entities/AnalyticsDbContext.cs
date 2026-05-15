using Microsoft.EntityFrameworkCore;

namespace TaxpayerAnalytics.Shared.Entities;

public class AnalyticsDbContext : DbContext
{
    public AnalyticsDbContext(DbContextOptions<AnalyticsDbContext> options) : base(options) { }

    public DbSet<CampaignMaster> Campaigns => Set<CampaignMaster>();
    public DbSet<TaxpayerRecipient> Recipients => Set<TaxpayerRecipient>();
    public DbSet<UserSession> Sessions => Set<UserSession>();
    public DbSet<EventLog> Events => Set<EventLog>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<CampaignMaster>(b =>
        {
            b.HasIndex(x => x.CampaignCode).IsUnique();
            b.HasIndex(x => x.Status);
            b.Property(x => x.Status).HasConversion<int>();
        });

        mb.Entity<TaxpayerRecipient>(b =>
        {
            b.HasIndex(x => new { x.CampaignId, x.NtnHash }).IsUnique();
            b.HasIndex(x => x.TrackingToken).IsUnique();
            b.HasIndex(x => x.CampaignId);
            b.HasOne(x => x.Campaign)
                .WithMany(c => c.Recipients)
                .HasForeignKey(x => x.CampaignId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<UserSession>(b =>
        {
            b.HasIndex(x => x.RecipientId);
            b.HasIndex(x => x.CampaignId);
            b.HasIndex(x => new { x.CampaignId, x.StartedAt });
            b.HasIndex(x => x.IsBot);
            b.Property(x => x.DeviceType).HasConversion<int>();
            b.HasOne(x => x.Recipient)
                .WithMany(r => r.Sessions)
                .HasForeignKey(x => x.RecipientId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<EventLog>(b =>
        {
            // Composite key so EventTime can be the partition column.
            b.HasKey(x => new { x.EventId, x.EventTime });
            b.Property(x => x.EventId).ValueGeneratedOnAdd();
            b.Property(x => x.EventType).HasConversion<int>();
            b.HasIndex(x => new { x.CampaignId, x.EventTime });
            b.HasIndex(x => new { x.SessionId, x.EventTime });
            b.HasIndex(x => new { x.RecipientId, x.EventTime });
            b.HasIndex(x => new { x.EventType, x.EventTime });
            b.HasIndex(x => x.ClientEventId);

            b.HasOne(x => x.Session)
                .WithMany(s => s.Events)
                .HasForeignKey(x => x.SessionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        base.OnModelCreating(mb);
    }
}
