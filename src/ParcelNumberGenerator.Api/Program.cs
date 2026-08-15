using ParcelNumberGenerator.Api.Endpoints;
using ParcelNumberGenerator.Api.Extensions;
using ParcelNumberGenerator.ServiceDefaults;

// A manifest, not configuration code (P9). Every line below names a capability; the wiring
// behind each one lives in the extension method it calls.

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddParcelNumberOptions();
builder.AddParcelNumberPersistence();
builder.AddParcelNumberAllocation();
builder.AddParcelNumberRateLimiting();

builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

WebApplication app = builder.Build();

app.EnsureProductionIsConfigured();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRateLimiter();

// Authentication is registered only when an issuer is configured, so the middleware is too.
// Adding it unconditionally would leave a pipeline stage that authenticates nobody and
// authorizes everybody, which reads as protection in a review and is not.
bool authenticationEnabled = builder.Configuration.IsJwtConfigured();
if (authenticationEnabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

app.MapDefaultEndpoints();
app.MapParcelNumberEndpoints(requireAuthorization: authenticationEnabled);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

await app.RunAsync();

/// <summary>Exposed so the test suite can host the real pipeline via WebApplicationFactory.</summary>
public partial class Program;
