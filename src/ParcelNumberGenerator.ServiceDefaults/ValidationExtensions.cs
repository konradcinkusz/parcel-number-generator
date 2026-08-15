using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace ParcelNumberGenerator.ServiceDefaults;

/// <summary>
/// SERVICE-API-PATTERNS §3 — one generic filter that runs DataAnnotations and returns a
/// ValidationProblem grouped by member. It lives in the kernel because every service
/// needs exactly this and a per-service copy is how the error shapes drift apart.
/// </summary>
public static class ValidationExtensions
{
    public static RouteHandlerBuilder WithValidation<TRequest>(this RouteHandlerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddEndpointFilter(async (context, next) =>
        {
            var request = context.Arguments.OfType<TRequest>().FirstOrDefault();

            if (request is null)
            {
                return await next(context);
            }

            var results = new List<ValidationResult>();
            var validationContext = new ValidationContext(request);

            if (Validator.TryValidateObject(request, validationContext, results, validateAllProperties: true))
            {
                return await next(context);
            }

            var errors = results
                .SelectMany(result => result.MemberNames.DefaultIfEmpty(string.Empty),
                    (result, member) => (Member: member, result.ErrorMessage))
                .GroupBy(entry => entry.Member, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(entry => entry.ErrorMessage ?? "Invalid value.").ToArray(),
                    StringComparer.Ordinal);

            return Results.ValidationProblem(errors);
        });

        return builder;
    }
}
