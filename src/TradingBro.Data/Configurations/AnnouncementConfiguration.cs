using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingBro.Core.Models;

namespace TradingBro.Data.Configurations;

public sealed class AnnouncementConfiguration : IEntityTypeConfiguration<Announcement>
{
    public void Configure(EntityTypeBuilder<Announcement> b)
    {
        b.ToTable("announcements");
        b.HasKey(x => x.Id);

        b.Property(x => x.Symbol).HasMaxLength(32).IsRequired();
        b.Property(x => x.Title).HasMaxLength(512).IsRequired();
        b.Property(x => x.Url).HasMaxLength(1024).IsRequired();
        b.Property(x => x.ExternalId).HasMaxLength(128).IsRequired();
        b.Property(x => x.Exchange).HasConversion<int>();
        b.Property(x => x.Market).HasConversion<int>();
        b.Property(x => x.EventType).HasConversion<int>();

        b.HasIndex(x => new { x.Exchange, x.Symbol, x.Market, x.EventType, x.AnnouncedAt })
            .IsUnique()
            .HasDatabaseName("ix_announcements_dedup");

        b.HasIndex(x => new { x.Exchange, x.ExternalId })
            .HasDatabaseName("ix_announcements_external");

        b.HasIndex(x => x.AnnouncedAt)
            .HasDatabaseName("ix_announcements_announced_at");
    }
}
