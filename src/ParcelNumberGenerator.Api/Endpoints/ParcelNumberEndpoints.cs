using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using ParcelNumberGenerator.Api.Configuration;
using ParcelNumberGenerator.Api.Contracts;
using ParcelNumberGenerator.Api.Extensions;
using ParcelNumberGenerator.Domain;
using ParcelNumberGenerator.Domain.Allocation;

namespace ParcelNumberGenerator.Api.Endpoints;

/// <summary>
/// Transport only: bind, authorize, delegate, map the result onto a status code. No
/// allocation logic lives here (P9).
/// </summary>
public static class ParcelNumberEndpoints
{
    public static IEndpointRouteBuilder MapParcelNumberEndpoints(
        this IEndpointRouteBuilder app,
        bool requireAuthorization)
    {
        RouteGroupBuilder numbers = app.MapGroup("/parcel-numbers").WithTags("Parcel numbers");

        numbers.MapPost("/", AllocateAsync)
            .WithName("AllocateParcelNumbers")
            .WithSummary("Issues one or more parcel numbers and records them as used.")
            .RequireRateLimiting(ServiceCollectionExtensions.AllocationRateLimitPolicy);

        numbers.MapGet("/{number:int}", GetStatusAsync)
            .WithName("GetParcelNumberStatus")
            .WithSummary("Reports whether a number has been issued.");

        RouteGroupBuilder pool = app.MapGroup("/pool").WithTags("Pool");

        pool.MapGet("/", GetPoolAsync)
            .WithName("GetPool")
            .WithSummary("Describes the pool and how much of it is left.");

        if (requireAuthorization)
        {
            numbers.RequireAuthorization();
            pool.RequireAuthorization();
        }

        return app;
    }

    private static async Task<Results<Created<AllocationResponse>, ProblemHttpResult>> AllocateAsync(
        ParcelNumberService service,
        IOptions<AllocationOptions> options,
        HttpContext httpContext,
        CancellationToken cancellationToken,
        int count = 1)
    {
        int maxBatchSize = options.Value.MaxBatchSize;
        if (count < 1 || count > maxBatchSize)
        {
            return TypedResults.Problem(
                title: "Invalid count",
                detail: $"count must be between 1 and {maxBatchSize}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        BatchAllocationResult result = await service.AllocateManyAsync(count, cancellationToken);

        // Nothing issued: the batch failed outright, so report the failure rather than a
        // successful response with an empty list.
        if (result.Numbers.Count == 0)
        {
            return Failure(result.Status, httpContext);
        }

        AllocationResponse response = new(
            result.Numbers,
            count,
            result.IsComplete,
            result.IsComplete ? null : Describe(result.Status));

        // A partial batch is still a success for what it issued — those numbers are claimed
        // and will never be issued again, so swallowing them into an error response would
        // silently burn them.
        return TypedResults.Created($"/parcel-numbers/{result.Numbers[0]}", response);
    }

    private static async Task<Ok<NumberStatusResponse>> GetStatusAsync(
        ParcelNumberService service,
        int number,
        CancellationToken cancellationToken)
    {
        bool used = await service.IsUsedAsync(number, cancellationToken);
        return TypedResults.Ok(new NumberStatusResponse(number, used, service.Pool.Contains(number)));
    }

    private static async Task<Ok<PoolResponse>> GetPoolAsync(
        ParcelNumberService service,
        CancellationToken cancellationToken)
    {
        PoolStatistics statistics = await service.GetStatisticsAsync(cancellationToken);

        return TypedResults.Ok(new PoolResponse(
            statistics.Range.From,
            statistics.Range.To,
            [.. statistics.Exclusions.Select(exclusion => new ExcludedRange(exclusion.From, exclusion.To))],
            statistics.Capacity,
            statistics.Used,
            statistics.Remaining,
            statistics.Density,
            service.StrategyName));
    }

    /// <summary>
    /// Maps a failed allocation onto a status code the caller can act on: 409 means stop
    /// asking, 503 means ask again.
    /// </summary>
    private static ProblemHttpResult Failure(AllocationStatus status, HttpContext httpContext)
    {
        if (status == AllocationStatus.Contended)
        {
            httpContext.Response.Headers.RetryAfter = "1";
            return TypedResults.Problem(
                title: "Allocation contended",
                detail: Describe(status),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return TypedResults.Problem(
            title: "Pool exhausted",
            detail: Describe(status),
            statusCode: StatusCodes.Status409Conflict);
    }

    private static string Describe(AllocationStatus status) => status switch
    {
        AllocationStatus.PoolExhausted =>
            "Every number in the configured pool has been issued. Widen the range or remove an exclusion.",
        AllocationStatus.Contended =>
            "The pool has free numbers but this request kept losing them to concurrent callers. Retry.",
        _ => "Allocated.",
    };
}
