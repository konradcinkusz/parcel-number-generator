using ParcelNumberGenerator.ServiceDefaults;
using ParcelNumberGenerator.Web.Extensions;

// A manifest, not configuration code (P9). The console is static files; the BFF proxy is
// the only server-side behaviour, and its wiring lives in the extension it names.

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddEstateProxy();
builder.Services.AddProblemDetails();

WebApplication app = builder.Build();

app.UseExceptionHandler();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapDefaultEndpoints();
app.MapEstateProxy();

await app.RunAsync();
