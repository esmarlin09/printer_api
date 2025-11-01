// Program.cs
using LitePrintApi.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Configurar servicios
builder.Services.AddApplicationServices();

var app = builder.Build();

// Configurar URLs
app.Urls.Add("http://localhost:5000");

// Configurar middleware
app.UseApplicationMiddleware();

// Configurar endpoints
app.MapApplicationEndpoints();

try
{
    app.Run();
}
catch (Exception ex)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogCritical(ex, "Application terminated unexpectedly");
    throw;
}
