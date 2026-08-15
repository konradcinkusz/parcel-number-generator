using Microsoft.EntityFrameworkCore;
using ParcelNumberGenerator.Contracts;
using ParcelNumberGenerator.Notifications.Data.Entities;

namespace ParcelNumberGenerator.Notifications.Data;

/// <summary>
/// P3 — the notification service owns this schema and no other service opens a
/// connection to it. Cross-context reads go over HTTP against the published contract in
/// <c>ParcelNumberGenerator.Contracts</c>.
/// </summary>
public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options)
    : DbContext(options)
{
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.ToTable("notifications");

            entity.HasKey(notification => notification.Id);

            // Database-assigned, so two concurrent writers cannot pick the same key the
            // way the legacy GetLastId() flow could.
            entity.Property(notification => notification.Id)
                .ValueGeneratedOnAdd();

            entity.Property(notification => notification.ParcelNumber)
                .HasMaxLength(ParcelNumberLimits.CanonicalLength);

            entity.Property(notification => notification.Body)
                .IsRequired()
                .HasMaxLength(NotificationLimits.MaxBodyLength);

            // Stored as the underlying int. Storing the name instead would make every
            // rename a data migration.
            entity.Property(notification => notification.Severity)
                .HasConversion<int>();

            entity.Property(notification => notification.RaisedBy)
                .HasConversion<int>();

            entity.Property(notification => notification.AcknowledgedBy)
                .HasMaxLength(128);

            entity.Property(notification => notification.CreatedAt)
                .IsRequired();

            entity.Ignore(notification => notification.IsOutstanding);

            // The operator dashboard's two queries: newest-first within a parcel, and
            // "what is still outstanding". Both are the whole workload of this service,
            // so both are indexed rather than left to a sequential scan.
            entity.HasIndex(notification => notification.ParcelNumber)
                .HasDatabaseName("ix_notifications_parcel_number");

            entity.HasIndex(notification => new { notification.AcknowledgementRequired, notification.AcknowledgedAt })
                .HasDatabaseName("ix_notifications_outstanding");

            entity.HasIndex(notification => notification.CreatedAt)
                .HasDatabaseName("ix_notifications_created_at");
        });
    }
}

/// <summary>
/// Length of the canonical parcel-number form, needed by both the schema and the parser.
/// </summary>
public static class ParcelNumberLimits
{
    /// <summary><c>PNG-12345678-5</c> — prefix, eight digits, check digit.</summary>
    public const int CanonicalLength = 14;
}
