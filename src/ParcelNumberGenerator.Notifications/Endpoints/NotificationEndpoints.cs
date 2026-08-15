using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ParcelNumberGenerator.Contracts;
using ParcelNumberGenerator.Notifications.Services;
using ParcelNumberGenerator.ServiceDefaults;

namespace ParcelNumberGenerator.Notifications.Endpoints;

/// <summary>
/// P9 — transport only: bind, authorize, delegate. No rule in this file decides anything
/// a second caller of <see cref="NotificationService"/> would need to decide again.
/// </summary>
public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(
        this IEndpointRouteBuilder app,
        bool requireAuthorization)
    {
        ArgumentNullException.ThrowIfNull(app);

        var operatorApi = app.MapGroup("/api/notifications")
            .RequireRateLimiting(RateLimitPolicies.Api)
            .WithTags("Notifications");

        var adminApi = app.MapGroup("/api/notifications/admin")
            .RequireRateLimiting(RateLimitPolicies.Sensitive)
            .WithTags("Notifications (admin)");

        // Conditional for the same reason the middleware is: with no issuer configured
        // there is no authentication service to consult, and a RequireAuthorization group
        // would reject every request rather than none. The startup guard is what stops
        // the open variant reaching production silently.
        if (requireAuthorization)
        {
            operatorApi.RequireAuthorization();
            adminApi.RequireAuthorization(policy => policy.RequireRole("Admin", "WarehouseManager"));
        }

        operatorApi.MapGet("/", ListAsync).WithName(EndpointNames.ListNotifications);
        operatorApi.MapGet("/{id:guid}", GetAsync).WithName(EndpointNames.GetNotification);

        operatorApi.MapPost("/", RaiseAsync)
            .WithName(EndpointNames.RaiseNotification)
            .WithValidation<RaiseNotificationRequest>();

        operatorApi.MapPut("/{id:guid}", UpdateAsync)
            .WithName(EndpointNames.UpdateNotification)
            .WithValidation<UpdateNotificationRequest>();

        operatorApi.MapPost("/{id:guid}/acknowledgement", AcknowledgeAsync)
            .WithName(EndpointNames.AcknowledgeNotification);

        adminApi.MapDelete("/{id:guid}", DeleteAsync).WithName(EndpointNames.DeleteNotification);

        return app;
    }

    private static async Task<IResult> ListAsync(
        NotificationService notifications,
        CancellationToken cancellationToken,
        int? page = null,
        int? limit = null,
        string? parcelNumber = null,
        bool outstandingOnly = false,
        NotificationSeverity? severity = null)
    {
        if (!NotificationQuery.TryCreate(page, limit, parcelNumber, outstandingOnly, severity, out var query))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["parcelNumber"] = [$"'{parcelNumber}' is not a recognized parcel number."],
            });
        }

        return Results.Ok(await notifications.GetPageAsync(query, cancellationToken));
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        NotificationService notifications,
        CancellationToken cancellationToken)
    {
        var notification = await notifications.GetAsync(id, cancellationToken);

        return notification is null ? Results.NotFound() : Results.Ok(notification);
    }

    private static async Task<IResult> RaiseAsync(
        RaiseNotificationRequest request,
        NotificationService notifications,
        CancellationToken cancellationToken)
    {
        var outcome = await notifications.RaiseAsync(request, cancellationToken);

        if (outcome.IsRejected)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["parcelNumber"] = [outcome.Rejection!],
            });
        }

        return Results.CreatedAtRoute(
            EndpointNames.GetNotification,
            new { id = outcome.Notification!.Id },
            outcome.Notification);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateNotificationRequest request,
        NotificationService notifications,
        CancellationToken cancellationToken)
    {
        var notification = await notifications.UpdateAsync(id, request, cancellationToken);

        return notification is null ? Results.NotFound() : Results.Ok(notification);
    }

    private static async Task<IResult> AcknowledgeAsync(
        Guid id,
        ClaimsPrincipal user,
        NotificationService notifications,
        CancellationToken cancellationToken)
    {
        var acknowledgedBy = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue("sub")
            ?? "unknown";

        var notification = await notifications.AcknowledgeAsync(id, acknowledgedBy, cancellationToken);

        return notification is null ? Results.NotFound() : Results.Ok(notification);
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        NotificationService notifications,
        CancellationToken cancellationToken) =>
        await notifications.DeleteAsync(id, cancellationToken) ? Results.NoContent() : Results.NotFound();
}
