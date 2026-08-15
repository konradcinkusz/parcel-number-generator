using Microsoft.EntityFrameworkCore;

namespace ParcelNumberGenerator.Data;

/// <summary>
/// The schema this service owns. No other service opens a connection to it (P3).
/// </summary>
public sealed class ParcelNumbersDbContext(DbContextOptions<ParcelNumbersDbContext> options)
    : DbContext(options)
{
    public DbSet<UsedNumber> UsedNumbers => Set<UsedNumber>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<UsedNumber>(entity =>
        {
            entity.ToTable("used_numbers");

            entity.HasKey(used => used.Number);

            // The application chooses the number; the database must not.
            entity.Property(used => used.Number)
                .HasColumnName("number")
                .ValueGeneratedNever();

            entity.Property(used => used.AllocatedAtUtc)
                .HasColumnName("allocated_at_utc")
                .IsRequired();
        });

        // No HasData. Reference data is seeded by a script per environment, not embedded in
        // every migration snapshot (P4) — which is what the legacy `CreateTable.sql` did,
        // shipping a thousand literal INSERTs as part of the schema definition.
    }
}
