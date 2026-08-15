using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using ParcelNumberGenerator.Contracts;
using ParcelNumberGenerator.Notifications.Data;
using ParcelNumberGenerator.Notifications.Data.Entities;

namespace ParcelNumberGenerator.Notifications.Tests;

/// <summary>
/// Asserts the mapping the migrations were generated from. These are the facts a
/// provider-specific migration bakes into DDL, so a change here that nobody regenerates
/// is exactly the drift P4 exists to prevent — and the InMemory provider will not catch
/// it, because it enforces none of them.
/// </summary>
public sealed class PersistenceMappingTests : IDisposable
{
    private readonly NotificationsDbContext _dbContext = new(
        new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase($"mapping-{Guid.NewGuid()}")
            .Options);

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public void The_table_is_named_explicitly_rather_than_taking_the_DbSet_name()
    {
        var entityType = _dbContext.Model.FindEntityType(typeof(Notification));

        Assert.NotNull(entityType);
        Assert.Equal("notifications", entityType.GetTableName());
    }

    [Theory]
    [InlineData(nameof(Notification.ParcelNumber), ParcelNumberLimits.CanonicalLength)]
    [InlineData(nameof(Notification.Body), NotificationLimits.MaxBodyLength)]
    [InlineData(nameof(Notification.AcknowledgedBy), 128)]
    public void Bounded_columns_declare_their_length(string propertyName, int expectedLength)
    {
        var property = _dbContext.Model
            .FindEntityType(typeof(Notification))!
            .FindProperty(propertyName);

        Assert.NotNull(property);
        Assert.Equal(expectedLength, property.GetMaxLength());
    }

    [Fact]
    public void Body_is_required_and_the_parcel_number_is_not()
    {
        var entityType = _dbContext.Model.FindEntityType(typeof(Notification))!;

        Assert.False(entityType.FindProperty(nameof(Notification.Body))!.IsNullable);

        // Not every notification is about a parcel.
        Assert.True(entityType.FindProperty(nameof(Notification.ParcelNumber))!.IsNullable);
    }

    [Theory]
    [InlineData(nameof(Notification.Severity))]
    [InlineData(nameof(Notification.RaisedBy))]
    public void Enums_are_stored_as_their_underlying_int_so_a_rename_is_not_a_data_migration(
        string propertyName)
    {
        var property = _dbContext.Model
            .FindEntityType(typeof(Notification))!
            .FindProperty(propertyName)!;

        Assert.Equal(typeof(int), property.GetProviderClrType());
    }

    [Fact]
    public void The_computed_outstanding_flag_is_not_persisted()
    {
        var entityType = _dbContext.Model.FindEntityType(typeof(Notification))!;

        // Storing it would let it disagree with the two columns it derives from.
        Assert.Null(entityType.FindProperty(nameof(Notification.IsOutstanding)));
    }

    [Theory]
    [InlineData("ix_notifications_parcel_number")]
    [InlineData("ix_notifications_outstanding")]
    [InlineData("ix_notifications_created_at")]
    public void The_queries_this_service_actually_runs_are_indexed(string indexName)
    {
        var indexes = _dbContext.Model
            .FindEntityType(typeof(Notification))!
            .GetIndexes()
            .Select(index => index.GetDatabaseName())
            .ToList();

        Assert.Contains(indexName, indexes);
    }

    [Fact]
    public void The_key_is_database_assigned()
    {
        var key = _dbContext.Model
            .FindEntityType(typeof(Notification))!
            .FindProperty(nameof(Notification.Id))!;

        Assert.Equal(ValueGenerated.OnAdd, key.ValueGenerated);
    }
}
