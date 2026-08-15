using Microsoft.EntityFrameworkCore;
using ParcelNumberGenerator.Contracts;
using ParcelNumberGenerator.Notifications.Data;
using ParcelNumberGenerator.Notifications.Services;

namespace ParcelNumberGenerator.Notifications.Tests;

/// <summary>
/// Characterisation tests for the behaviour carried over from the legacy WinForms
/// application, plus the defects that behaviour had. Each test named
/// <c>Legacy_*</c> fails against the old implementation by construction — they are the
/// record of what the migration was actually for.
/// </summary>
public sealed class NotificationServiceTests : IAsyncDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 9, 0, 0, TimeSpan.Zero);

    private readonly NotificationsDbContext _dbContext;
    private readonly FixedTimeProvider _time = new(Now);
    private readonly RecordingChannel _channel = new();

    public NotificationServiceTests()
    {
        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase($"notifications-{Guid.NewGuid()}")
            .Options;

        _dbContext = new NotificationsDbContext(options);
    }

    private NotificationService CreateService() => new(_dbContext, [_channel], _time);

    public async ValueTask DisposeAsync() => await _dbContext.DisposeAsync();

    [Fact]
    public async Task Raise_stores_the_canonical_parcel_number_not_the_dialect_it_arrived_in()
    {
        var service = CreateService();

        var outcome = await service.RaiseAsync(
            new RaiseNotificationRequest
            {
                ParcelNumber = "wms/12345678",
                Body = "Damaged on arrival",
                Severity = NotificationSeverity.Error,
                RaisedBy = ParcelEventKind.Received,
            },
            TestContext.Current.CancellationToken);

        Assert.False(outcome.IsRejected);
        Assert.Equal("PNG-12345678-2", outcome.Notification!.ParcelNumber);

        var stored = await _dbContext.Notifications.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("PNG-12345678-2", stored.ParcelNumber);
    }

    [Fact]
    public async Task Raise_rejects_an_unparseable_parcel_number_without_persisting_anything()
    {
        var service = CreateService();

        var outcome = await service.RaiseAsync(
            new RaiseNotificationRequest { ParcelNumber = "DHL/999", Body = "Anything" },
            TestContext.Current.CancellationToken);

        Assert.True(outcome.IsRejected);
        Assert.Empty(await _dbContext.Notifications.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Raise_accepts_a_notification_that_is_not_parcel_scoped()
    {
        var service = CreateService();

        var outcome = await service.RaiseAsync(
            new RaiseNotificationRequest { ParcelNumber = null, Body = "Shift starts 06:00" },
            TestContext.Current.CancellationToken);

        Assert.False(outcome.IsRejected);
        Assert.Null(outcome.Notification!.ParcelNumber);
    }

    [Fact]
    public async Task Raise_persists_before_it_delivers()
    {
        var service = CreateService();

        var outcome = await service.RaiseAsync(
            new RaiseNotificationRequest { Body = "Pick face empty" },
            TestContext.Current.CancellationToken);

        // The channel saw an id, which only exists because the row was already written.
        var delivered = Assert.Single(_channel.Delivered);
        Assert.Equal(outcome.Notification!.Id, delivered.Id);
        Assert.NotEqual(Guid.Empty, delivered.Id);
    }

    [Fact]
    public async Task Raise_reports_a_channel_failure_without_losing_the_notification()
    {
        var failing = new FailingChannel();
        var service = new NotificationService(_dbContext, [_channel, failing], _time);

        var outcome = await service.RaiseAsync(
            new RaiseNotificationRequest { Body = "Dispatch bay blocked" },
            TestContext.Current.CancellationToken);

        Assert.False(outcome.IsRejected);
        Assert.Contains(outcome.Deliveries, delivery => !delivery.Delivered);
        Assert.Single(await _dbContext.Notifications.ToListAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The legacy <c>MessagesRepository.UpdateMessage</c> deleted the row and re-inserted
    /// it, so every edit changed the primary key and orphaned anything referencing it.
    /// </summary>
    [Fact]
    public async Task Legacy_update_no_longer_changes_the_identifier()
    {
        var service = CreateService();

        var raised = await service.RaiseAsync(
            new RaiseNotificationRequest { Body = "Original", Severity = NotificationSeverity.Warning },
            TestContext.Current.CancellationToken);

        var id = raised.Notification!.Id;

        var updated = await service.UpdateAsync(
            id,
            new UpdateNotificationRequest { Body = "Amended", Severity = NotificationSeverity.Error },
            TestContext.Current.CancellationToken);

        Assert.NotNull(updated);
        Assert.Equal(id, updated.Id);
        Assert.Equal("Amended", updated.Body);
        Assert.Equal(NotificationSeverity.Error, updated.Severity);

        // One row, not a delete plus an insert.
        Assert.Single(await _dbContext.Notifications.ToListAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The legacy update dropped the creation data its copy constructor did not carry.
    /// </summary>
    [Fact]
    public async Task Legacy_update_no_longer_discards_fields_it_does_not_touch()
    {
        var service = CreateService();

        var raised = await service.RaiseAsync(
            new RaiseNotificationRequest
            {
                ParcelNumber = "12345678",
                Body = "Original",
                RaisedBy = ParcelEventKind.Picked,
            },
            TestContext.Current.CancellationToken);

        var updated = await service.UpdateAsync(
            raised.Notification!.Id,
            new UpdateNotificationRequest { Body = "Amended" },
            TestContext.Current.CancellationToken);

        Assert.Equal("PNG-12345678-2", updated!.ParcelNumber);
        Assert.Equal(ParcelEventKind.Picked, updated.RaisedBy);
        Assert.Equal(Now, updated.CreatedAt);
    }

    /// <summary>
    /// The legacy client assigned ids from <c>GetLastId()</c>, so two senders racing each
    /// other picked the same integer. Keys are database-assigned now, and every raise gets
    /// a distinct one whether or not the callers overlap.
    /// </summary>
    [Fact]
    public async Task Legacy_client_assigned_identifiers_no_longer_collide()
    {
        var service = CreateService();

        var ids = new List<Guid>();

        for (var index = 0; index < 25; index++)
        {
            var outcome = await service.RaiseAsync(
                new RaiseNotificationRequest { Body = $"Notification {index}" },
                TestContext.Current.CancellationToken);

            ids.Add(outcome.Notification!.Id);
        }

        Assert.Equal(25, ids.Distinct().Count());
    }

    [Fact]
    public async Task Acknowledgement_records_when_and_by_whom()
    {
        var service = CreateService();

        var raised = await service.RaiseAsync(
            new RaiseNotificationRequest { Body = "Confirm receipt", AcknowledgementRequired = true },
            TestContext.Current.CancellationToken);

        Assert.True(raised.Notification!.IsOutstanding);

        var acknowledged = await service.AcknowledgeAsync(
            raised.Notification.Id, "operator-7", TestContext.Current.CancellationToken);

        Assert.Equal(Now, acknowledged!.AcknowledgedAt);
        Assert.Equal("operator-7", acknowledged.AcknowledgedBy);
        Assert.False(acknowledged.IsOutstanding);
    }

    [Fact]
    public async Task Acknowledgement_is_idempotent_and_keeps_the_first_timestamp()
    {
        var service = CreateService();

        var raised = await service.RaiseAsync(
            new RaiseNotificationRequest { Body = "Confirm receipt", AcknowledgementRequired = true },
            TestContext.Current.CancellationToken);

        await service.AcknowledgeAsync(raised.Notification!.Id, "operator-7", TestContext.Current.CancellationToken);

        _time.Advance(TimeSpan.FromHours(3));

        var second = await service.AcknowledgeAsync(
            raised.Notification.Id, "operator-9", TestContext.Current.CancellationToken);

        Assert.Equal(Now, second!.AcknowledgedAt);
        Assert.Equal("operator-7", second.AcknowledgedBy);
    }

    [Fact]
    public async Task A_notification_that_needs_no_acknowledgement_is_never_outstanding()
    {
        var service = CreateService();

        var raised = await service.RaiseAsync(
            new RaiseNotificationRequest { Body = "FYI", AcknowledgementRequired = false },
            TestContext.Current.CancellationToken);

        Assert.False(raised.Notification!.IsOutstanding);
    }

    [Fact]
    public async Task Unknown_identifiers_are_reported_rather_than_thrown()
    {
        var service = CreateService();
        var unknown = Guid.CreateVersion7();

        Assert.Null(await service.GetAsync(unknown, TestContext.Current.CancellationToken));
        Assert.Null(await service.AcknowledgeAsync(unknown, "me", TestContext.Current.CancellationToken));
        Assert.Null(await service.UpdateAsync(
            unknown, new UpdateNotificationRequest { Body = "x" }, TestContext.Current.CancellationToken));
    }
}
