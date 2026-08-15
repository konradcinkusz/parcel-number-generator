using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace ParcelNumberGenerator.ServiceDefaults;

/// <summary>
/// P2 — OpenAPI is kernel plumbing so every service in the estate documents its bearer
/// scheme identically and a generated client works the same way against all of them.
/// Built on the framework's own OpenAPI support rather than a third-party generator:
/// the document is produced from the same endpoint metadata the router uses, so it
/// cannot describe a route that does not exist.
/// </summary>
public static class OpenApiExtensions
{
    private const string BearerScheme = "Bearer";

    public static IServiceCollection AddOpenApiWithJwt(
        this IServiceCollection services,
        string title,
        string version,
        string description)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        services.AddOpenApi(version, options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new OpenApiInfo
                {
                    Title = title,
                    Version = version,
                    Description = description,
                };

                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes[BearerScheme] = new OpenApiSecurityScheme
                {
                    Name = "Authorization",
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    In = ParameterLocation.Header,
                    Description = "Bearer token issued by the estate's identity service.",
                };

                document.Security =
                [
                    new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference(BearerScheme, document)] = [],
                    },
                ];

                return Task.CompletedTask;
            });
        });

        return services;
    }
}
