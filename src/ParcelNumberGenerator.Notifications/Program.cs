using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ParcelNumberGenerator.Notifications.Endpoints;
using ParcelNumberGenerator.Notifications.Extensions;
using ParcelNumberGenerator.ServiceDefaults;

// P9 — Program.cs is a manifest. Every line below is one capability; the configuration
// behind each of them lives in the extension method it names.

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddNotificationPersistence();
builder.Services.AddNotificationServices(builder.Configuration);
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddCorsPolicy(builder.Configuration, CorsPolicies.Frontend);
builder.Services.AddStandardRateLimiting();
builder.Services.AddOpenApiWithJwt(
    title: "PNG Notifications",
    version: "v1",
    description: "Notifications raised against parcels and the warehouse that handles them.");
builder.Services.AddProblemDetails();

var app = builder.Build();

app.EnsureProductionIsConfigured();

app.UseExceptionHandler();
app.UseCors(CorsPolicies.Frontend);
app.UseRateLimiter();

// Authentication is registered only when an issuer is configured (P8), so the middleware
// is too. Adding it unconditionally would leave a pipeline stage that authenticates
// nobody and authorizes everybody, which reads as protection in a review and is not.
// The startup guard above is what keeps this fallback out of production by accident.
bool authenticationEnabled = builder.Configuration.IsJwtConfigured();
if (authenticationEnabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

app.MapDefaultEndpoints();
app.MapNotificationEndpoints(requireAuthorization: authenticationEnabled);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

await app.RunAsync();

// Exposed so the test project's WebApplicationFactory has a TEntryPoint to name. Top-level
// statements generate this class in the global namespace, so the partial must live there too.
public partial class Program;
