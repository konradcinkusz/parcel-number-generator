using Microsoft.EntityFrameworkCore;
using ParcelNumberGenerator.Contracts;
using ParcelNumberGenerator.Notifications.Data;
using ParcelNumberGenerator.Notifications.Services;

namespace ParcelNumberGenerator.Notifications.Tests;

/// <summary>
/// SERVICE-API-PATTERNS §4 — clamping is a DoS control, not a nicety, and the page's
/// counts come from one round trip rather than one <c>Count()</c> per statistic.
/// </summary>
public sealed class NotificationQueryTests : IAsyncDisposable
{
    private readonly NotificationsDbContext _dbContext;
    private readonly FixedTimeProvider _time = new(new DateTimeOffset(2026, 8, 15, 9, 0, 0, TimeSpan.Zero));

    public NotificationQueryTests()
    {
        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase($"notifications-{Guid.NewGuid()}")
            .Options;

        _dbContext = new NotificationsDbContext(options);
    }

    private NotificationService CreateService() => new(_dbContext, [], _time);

    public async ValueTask DisposeAsync() => await _dbContext.DisposeAsync();

    [Theory]
    [InlineData(null, null, 1, NotificationLimits.DefaultPageSize)]
    [InlineData(0, 0, 1, 1)]
    [InlineData(-5, -5, 1, 1)]
    // The one-line outage: an unclamped limit of two million.
    [InlineData(1, 2_000_000, 1, NotificationLimits.MaxPageSize)]
    [InlineData(3, 50, 3, 50)]
    public void Page_and_limit_are_clamped_never_rejected(
        int? page,
        int? limit,
        int expectedPage,
        int expectedLimit)
    {
        Assert.True(NotificationQuery.TryCreate(
            page, limit, parcelNumber: null, outstandingOnly: false, severity: null, out var query));

        Assert.Equal(expectedPage, query.Page);
        Assert.Equal(expectedLimit, query.Limit);
    }

    [Fact]
    public void A_parcel_number_filter_is_normalized_like_every_other_parcel_number()
    {
        Assert.True(NotificationQuery.TryCreate(
            page: 1, limit: 10, parcelNumber: "wms/12345678", outstandingOnly: false, severity: null,
            out var query));

        Assert.Equal("PNG-12345678-2", query.ParcelNumber);
    }

    [Fact]
    public void An_unparseable_parcel_number_filter_is_rejected()
    {
        Assert.False(NotificationQuery.TryCreate(
            page: 1, limit: 10, parcelNumber: "DHL/999", outstandingOnly: false, severity: null, out _));
    }

    [Fact]
    public async Task Page_counts_describe_the_whole_filtered_set_not_just_the_page()
    {
        var service = CreateService();

        for (var index = 0; index < 12; index++)
        {
            await service.RaiseAsync(
                new RaiseNotificationRequest
                {
                    Body = $"Notification {index}",
                    // Four of the twelve need acknowledging.
                    AcknowledgementRequired = index % 3 == 0,
                },
                TestContext.Current.CancellationToken);
        }

        Assert.True(NotificationQuery.TryCreate(
            page: 1, limit: 5, parcelNumber: null, outstandingOnly: false, severity: null, out var query));

        var page = await service.GetPageAsync(query, TestContext.Current.CancellationToken);

        Assert.Equal(5, page.Items.Count);
        Assert.Equal(12, page.Total);
        Assert.Equal(4, page.Outstanding);
    }

    [Fact]
    public async Task Filtering_by_parcel_narrows_the_counts_too()
    {
        var service = CreateService();

        await service.RaiseAsync(
            new RaiseNotificationRequest { ParcelNumber = "12345678", Body = "About this parcel" },
            TestContext.Current.CancellationToken);

        await service.RaiseAsync(
            new RaiseNotificationRequest { ParcelNumber = "99999999", Body = "About another" },
            TestContext.Current.CancellationToken);

        Assert.True(NotificationQuery.TryCreate(
            page: 1, limit: 10, parcelNumber: "PNG-12345678-2", outstandingOnly: false, severity: null,
            out var query));

        var page = await service.GetPageAsync(query, TestContext.Current.CancellationToken);

        Assert.Equal(1, page.Total);
        Assert.Equal("PNG-12345678-2", Assert.Single(page.Items).ParcelNumber);
    }

    [Fact]
    public async Task Outstanding_only_returns_what_still_needs_a_human()
    {
        var service = CreateService();

        var needsAck = await service.RaiseAsync(
            new RaiseNotificationRequest { Body = "Needs ack", AcknowledgementRequired = true },
            TestContext.Current.CancellationToken);

        await service.RaiseAsync(
            new RaiseNotificationRequest { Body = "No ack needed" },
            TestContext.Current.CancellationToken);

        Assert.True(NotificationQuery.TryCreate(
            page: 1, limit: 10, parcelNumber: null, outstandingOnly: true, severity: null, out var query));

        var before = await service.GetPageAsync(query, TestContext.Current.CancellationToken);
        Assert.Equal(1, before.Total);

        await service.AcknowledgeAsync(
            needsAck.Notification!.Id, "operator-1", TestContext.Current.CancellationToken);

        var after = await service.GetPageAsync(query, TestContext.Current.CancellationToken);
        Assert.Equal(0, after.Total);
    }

    [Fact]
    public async Task Pinned_notifications_sort_above_the_rest()
    {
        var service = CreateService();

        await service.RaiseAsync(
            new RaiseNotificationRequest { Body = "Ordinary" },
            TestContext.Current.CancellationToken);

        _time.Advance(TimeSpan.FromMinutes(1));

        await service.RaiseAsync(
            new RaiseNotificationRequest { Body = "Pinned", Pinned = true },
            TestContext.Current.CancellationToken);

        _time.Advance(TimeSpan.FromMinutes(1));

        await service.RaiseAsync(
            new RaiseNotificationRequest { Body = "Newest ordinary" },
            TestContext.Current.CancellationToken);

        Assert.True(NotificationQuery.TryCreate(
            page: 1, limit: 10, parcelNumber: null, outstandingOnly: false, severity: null, out var query));

        var page = await service.GetPageAsync(query, TestContext.Current.CancellationToken);

        Assert.Equal("Pinned", page.Items[0].Body);
        Assert.Equal("Newest ordinary", page.Items[1].Body);
        Assert.Equal("Ordinary", page.Items[2].Body);
    }

    [Fact]
    public async Task An_empty_result_set_reports_zero_rather_than_failing()
    {
        var service = CreateService();

        Assert.True(NotificationQuery.TryCreate(
            page: 1, limit: 10, parcelNumber: null, outstandingOnly: false, severity: null, out var query));

        var page = await service.GetPageAsync(query, TestContext.Current.CancellationToken);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.Total);
        Assert.Equal(0, page.Outstanding);
    }
}
