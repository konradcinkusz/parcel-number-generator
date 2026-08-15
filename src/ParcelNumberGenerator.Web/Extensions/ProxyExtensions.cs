using System.Globalization;

namespace ParcelNumberGenerator.Web.Extensions;

/// <summary>
/// The BFF proxy (FRONTEND-BFF §5). One catch-all route per backend fronts the estate, so
/// the browser has exactly one base URL — its own origin — and never learns where the
/// services live or negotiates CORS with them.
/// </summary>
/// <remarks>
/// <para>
/// Each backend resolves through an ordered candidate ladder: explicit configuration →
/// the orchestrator's service-discovery variables → a localhost development fallback.
/// The platform rung is the explicit variable — every fly.toml sets
/// <c>Services__Generator__BaseUrl</c> and friends — so one code path serves a laptop,
/// the Aspire AppHost and Fly.io with zero per-environment code.
/// </para>
/// <para>
/// The proxy deliberately has no retry handler. Allocation is not idempotent — a retried
/// POST that timed out on the response burns parcel numbers — so the caller decides what
/// to repeat, exactly as it would talking to the service directly.
/// </para>
/// </remarks>
public static class ProxyExtensions
{
    /// <summary>
    /// Covers a scale-to-zero backend's cold start (P7): machine wake plus .NET boot. A
    /// timeout is reported as 504 rather than left to the browser's own opaque failure.
    /// </summary>
    private static readonly TimeSpan UpstreamTimeout = TimeSpan.FromSeconds(75);

    private static readonly string[] HopByHopHeaders =
    [
        "Connection", "Keep-Alive", "Proxy-Authenticate", "Proxy-Authorization",
        "Proxy-Connection", "TE", "Trailer", "Transfer-Encoding", "Upgrade", "Host",
    ];

    public static WebApplicationBuilder AddEstateProxy(this WebApplicationBuilder builder)
    {
        // One long-lived client, built by hand rather than through the factory. The kernel
        // gives every factory client the standard resilience handler by default — right for
        // service code, wrong for a proxy: nothing here may retry a non-idempotent
        // allocation, and the handler's 10-second attempt timeout would undercut the
        // cold-start budget above. PooledConnectionLifetime keeps DNS rotation working,
        // which is the one factory behaviour worth keeping.
        builder.Services.AddSingleton(new EstateProxyClient(new HttpClient(
            new SocketsHttpHandler
            {
                // A redirect between services is a configuration bug; following one would
                // silently convert POST to GET (SERVICE-API-PATTERNS §5).
                AllowAutoRedirect = false,
                UseCookies = false,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            })
        {
            Timeout = UpstreamTimeout,
        }));

        builder.Services.AddSingleton(new BackendDirectory(builder.Configuration));

        return builder;
    }

    public static IEndpointRouteBuilder MapEstateProxy(this IEndpointRouteBuilder app)
    {
        // Path prefix → backend, with the prefix the backend itself expects restored on
        // the way through: the generator serves at its root, the notification service
        // under /api/notifications.
        app.Map("/api/generator/{**path}", (HttpContext context, BackendDirectory backends, string? path) =>
            ForwardAsync(context, backends.Generator, path ?? string.Empty));

        app.Map("/api/notifications/{**path}", (HttpContext context, BackendDirectory backends, string? path) =>
            ForwardAsync(
                context,
                backends.Notifications,
                string.IsNullOrEmpty(path) ? "api/notifications" : $"api/notifications/{path}"));

        return app;
    }

    private static async Task<IResult> ForwardAsync(HttpContext context, BackendEndpoint backend, string path)
    {
        var upstreamUri = new Uri(backend.BaseAddress, path + context.Request.QueryString.Value);

        using var upstreamRequest = new HttpRequestMessage(new HttpMethod(context.Request.Method), upstreamUri);

        bool hasBody = context.Request.ContentLength is > 0
            || context.Request.Headers.ContainsKey("Transfer-Encoding");

        if (hasBody)
        {
            upstreamRequest.Content = new StreamContent(context.Request.Body);
        }

        foreach (var header in context.Request.Headers)
        {
            if (HopByHopHeaders.Contains(header.Key, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!upstreamRequest.Headers.TryAddWithoutValidation(header.Key, [.. header.Value]))
            {
                upstreamRequest.Content?.Headers.TryAddWithoutValidation(header.Key, [.. header.Value]);
            }
        }

        HttpClient httpClient = context.RequestServices
            .GetRequiredService<EstateProxyClient>().Client;

        try
        {
            // ResponseHeadersRead, so a large response streams through rather than being
            // buffered whole in the BFF (FRONTEND-BFF §5).
            using var upstreamResponse = await httpClient.SendAsync(
                upstreamRequest,
                HttpCompletionOption.ResponseHeadersRead,
                context.RequestAborted);

            context.Response.StatusCode = (int)upstreamResponse.StatusCode;

            foreach (var header in upstreamResponse.Headers.Concat(upstreamResponse.Content.Headers))
            {
                if (!HopByHopHeaders.Contains(header.Key, StringComparer.OrdinalIgnoreCase))
                {
                    context.Response.Headers[header.Key] = header.Value.ToArray();
                }
            }

            await upstreamResponse.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
            return Results.Empty;
        }
        catch (HttpRequestException exception)
        {
            return Results.Problem(
                title: "Backend unreachable",
                detail: $"The {backend.Name} service did not answer: {exception.Message}",
                statusCode: StatusCodes.Status502BadGateway);
        }
        catch (TaskCanceledException) when (!context.RequestAborted.IsCancellationRequested)
        {
            return Results.Problem(
                title: "Backend timed out",
                detail: $"The {backend.Name} service did not answer within "
                    + $"{UpstreamTimeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)}s. "
                    + "A scaled-to-zero machine may still be waking; retrying is reasonable.",
                statusCode: StatusCodes.Status504GatewayTimeout);
        }
    }
}

/// <summary>A backend the proxy can reach: where it is, and what to call it in an error.</summary>
public sealed record BackendEndpoint(Uri BaseAddress, string Name);

/// <summary>The proxy's own HttpClient, deliberately outside the factory's default handlers.</summary>
public sealed class EstateProxyClient(HttpClient client)
{
    public HttpClient Client { get; } = client;
}

/// <summary>
/// Resolves each backend's base address once, through the candidate ladder. Presence
/// decides, not probing: the first rung with a value wins, so resolution is deterministic
/// and visible in configuration rather than dependent on what happened to answer first.
/// </summary>
public sealed class BackendDirectory
{
    public BackendDirectory(IConfiguration configuration)
    {
        Generator = Resolve(
            configuration,
            name: "generator",
            explicitKey: "Services:Generator:BaseUrl",
            discoveryName: "api",
            developmentFallback: "http://localhost:5180");

        Notifications = Resolve(
            configuration,
            name: "notifications",
            explicitKey: "Services:Notifications:BaseUrl",
            discoveryName: "notifications",
            developmentFallback: "http://localhost:5181");
    }

    public BackendEndpoint Generator { get; }

    public BackendEndpoint Notifications { get; }

    private static BackendEndpoint Resolve(
        IConfiguration configuration,
        string name,
        string explicitKey,
        string discoveryName,
        string developmentFallback)
    {
        // Rung 1 — explicit. This is also the platform rung: fly.toml sets it per app.
        // Rung 2 — Aspire service discovery, injected by the AppHost's WithReference.
        // Rung 3 — bare `dotnet run` on a laptop, three processes on known ports.
        // Present-but-empty counts as absent: appsettings.json documents the keys with
        // empty values, and an empty string would otherwise parse as a file:// URI.
        string address = FirstNonEmpty(
            configuration[explicitKey],
            configuration[$"services:{discoveryName}:https:0"],
            configuration[$"services:{discoveryName}:http:0"])
            ?? developmentFallback;

        return new BackendEndpoint(
            new Uri(address.EndsWith('/') ? address : address + "/", UriKind.Absolute),
            name);
    }

    private static string? FirstNonEmpty(params string?[] candidates)
    {
        foreach (string? candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
