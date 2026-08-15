using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace ParcelNumberGenerator.ServiceDefaults;

/// <summary>
/// Bearer-token validation against an external identity provider's JWKS endpoint.
/// </summary>
/// <remarks>
/// <para>
/// This service holds no key material and cannot mint a token — it only verifies. That is
/// the point of P5, and it is structural rather than a matter of discipline: with a shared
/// symmetric secret, "can verify" and "can forge" are the same capability, so any service
/// holding one can issue a token for any user. Asymmetric signing plus a published JWKS
/// makes the mistake impossible to make here.
/// </para>
/// <para>
/// Registration is conditional on <c>Jwt:Authority</c> being present (P8). Whether its
/// absence is acceptable is an environment question, and the API answers it at startup
/// rather than here.
/// </para>
/// </remarks>
public static class AuthenticationExtensions
{
    public const string AuthoritySection = "Jwt";

    public static bool IsJwtConfigured(this IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration[$"{AuthoritySection}:Authority"]);

    public static IServiceCollection AddJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (!configuration.IsJwtConfigured())
        {
            return services;
        }

        IConfigurationSection section = configuration.GetSection(AuthoritySection);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Authority drives OIDC discovery, so the signing keys are fetched from the
                // issuer's JWKS and refreshed on rotation. No key is configured here.
                options.Authority = section["Authority"];
                options.RequireHttpsMetadata = !bool.TryParse(section["AllowHttpMetadata"], out bool allowHttp) || !allowHttp;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = section["Issuer"] ?? section["Authority"],
                    ValidateAudience = !string.IsNullOrWhiteSpace(section["Audience"]),
                    ValidAudience = section["Audience"],
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        services.AddAuthorization();
        return services;
    }
}
