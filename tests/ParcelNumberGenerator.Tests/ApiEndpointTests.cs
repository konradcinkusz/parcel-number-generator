using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using ParcelNumberGenerator.Api.Contracts;
using ParcelNumberGenerator.Tests.Infrastructure;

namespace ParcelNumberGenerator.Tests;

/// <summary>
/// The service through its own HTTP surface, hosted from the real <c>Program.cs</c>.
/// </summary>
public sealed class ApiEndpointTests
{
    [Fact]
    public async Task Health_and_liveness_endpoints_answer()
    {
        using ApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri("/health", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri("/alive", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Allocating_returns_a_number_inside_the_configured_pool()
    {
        using ApiFactory factory = new(from: 500, to: 600);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsync(new Uri("/parcel-numbers", UriKind.Relative), null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        AllocationResponse? body = await response.Content.ReadFromJsonAsync<AllocationResponse>();
        Assert.NotNull(body);
        Assert.True(body.Complete);
        Assert.Single(body.Numbers);
        Assert.InRange(body.Numbers[0], 500, 600);
    }

    [Fact]
    public async Task A_batch_never_repeats_a_number()
    {
        using ApiFactory factory = new(from: 1, to: 50);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsync(
            new Uri("/parcel-numbers?count=50", UriKind.Relative), null);

        AllocationResponse? body = await response.Content.ReadFromJsonAsync<AllocationResponse>();
        Assert.NotNull(body);
        Assert.True(body.Complete);

        // The whole point of the service: 50 draws from a pool of 50 must be the pool.
        Assert.Equal(50, body.Numbers.Count);
        Assert.Equal(50, body.Numbers.Distinct().Count());
        Assert.Equal([.. Enumerable.Range(1, 50)], [.. body.Numbers.Order()]);
    }

    [Fact]
    public async Task A_drained_pool_reports_conflict_rather_than_reissuing()
    {
        using ApiFactory factory = new(from: 1, to: 5);
        using HttpClient client = factory.CreateClient();

        await client.PostAsync(new Uri("/parcel-numbers?count=5", UriKind.Relative), null);
        HttpResponseMessage response = await client.PostAsync(new Uri("/parcel-numbers", UriKind.Relative), null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task A_batch_larger_than_the_pool_returns_what_it_could_issue()
    {
        using ApiFactory factory = new(from: 1, to: 5);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsync(
            new Uri("/parcel-numbers?count=10", UriKind.Relative), null);

        // Partial success is still success for the five numbers that are now permanently
        // issued. Reporting it as an error would burn them silently.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        AllocationResponse? body = await response.Content.ReadFromJsonAsync<AllocationResponse>();
        Assert.NotNull(body);
        Assert.False(body.Complete);
        Assert.Equal(10, body.Requested);
        Assert.Equal(5, body.Numbers.Count);
        Assert.NotNull(body.Reason);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1001)]
    public async Task An_out_of_bounds_count_is_rejected(int count)
    {
        using ApiFactory factory = new();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsync(
            new Uri($"/parcel-numbers?count={count}", UriKind.Relative), null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Allocation_never_returns_an_excluded_number()
    {
        using ApiFactory factory = new(from: 1, to: 20, exclusions: [(5, 15)]);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsync(
            new Uri("/parcel-numbers?count=9", UriKind.Relative), null);

        AllocationResponse? body = await response.Content.ReadFromJsonAsync<AllocationResponse>();
        Assert.NotNull(body);
        Assert.Equal([1, 2, 3, 4, 16, 17, 18, 19, 20], [.. body.Numbers.Order()]);
    }

    [Fact]
    public async Task The_pool_endpoint_reports_capacity_and_what_is_left()
    {
        using ApiFactory factory = new(from: 1, to: 100, exclusions: [(50, 59)]);
        using HttpClient client = factory.CreateClient();

        await client.PostAsync(new Uri("/parcel-numbers?count=10", UriKind.Relative), null);

        PoolResponse? pool = await client.GetFromJsonAsync<PoolResponse>(new Uri("/pool", UriKind.Relative));

        Assert.NotNull(pool);
        Assert.Equal(90, pool.Capacity);
        Assert.Equal(10, pool.Used);
        Assert.Equal(80, pool.Remaining);
        Assert.Equal([new ExcludedRange(50, 59)], pool.Exclusions);
        Assert.Equal("adaptive", pool.Strategy);
    }

    [Fact]
    public async Task An_issued_number_reads_back_as_used()
    {
        using ApiFactory factory = new(from: 1, to: 10);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage allocation = await client.PostAsync(new Uri("/parcel-numbers", UriKind.Relative), null);
        AllocationResponse? body = await allocation.Content.ReadFromJsonAsync<AllocationResponse>();
        Assert.NotNull(body);
        int issued = body.Numbers[0];

        NumberStatusResponse? status = await client.GetFromJsonAsync<NumberStatusResponse>(
            new Uri($"/parcel-numbers/{issued}", UriKind.Relative));

        Assert.NotNull(status);
        Assert.True(status.Used);
        Assert.True(status.InPool);
    }

    [Fact]
    public async Task A_number_outside_the_pool_reports_that_it_is_not_allocatable()
    {
        using ApiFactory factory = new(from: 1, to: 10);
        using HttpClient client = factory.CreateClient();

        NumberStatusResponse? status = await client.GetFromJsonAsync<NumberStatusResponse>(
            new Uri("/parcel-numbers/9999", UriKind.Relative));

        Assert.NotNull(status);
        Assert.False(status.Used);
        Assert.False(status.InPool);
    }

    [Fact]
    public async Task The_sequential_strategy_serves_the_same_contract()
    {
        using ApiFactory factory = new ApiFactory(from: 1, to: 20)
            .With("Allocation:Strategy", "sequential-scan");
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsync(
            new Uri("/parcel-numbers?count=20", UriKind.Relative), null);

        AllocationResponse? body = await response.Content.ReadFromJsonAsync<AllocationResponse>();
        Assert.NotNull(body);
        Assert.Equal([.. Enumerable.Range(1, 20)], [.. body.Numbers.Order()]);

        PoolResponse? pool = await client.GetFromJsonAsync<PoolResponse>(new Uri("/pool", UriKind.Relative));
        Assert.Equal("sequential-scan", pool?.Strategy);
    }

    [Fact]
    public async Task An_unknown_strategy_stops_the_host_from_starting()
    {
        using ApiFactory factory = new ApiFactory().With("Allocation:Strategy", "not-a-strategy");

        // ValidateOnStart, so this is a startup failure with a message naming the valid
        // options — not a 500 on the first allocation in production.
        OptionsValidationException exception =
            Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
        Assert.Contains("random-probe", string.Join(' ', exception.Failures), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_inverted_pool_range_stops_the_host_from_starting()
    {
        using ApiFactory factory = new(from: 100, to: 1);

        Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
    }
}
