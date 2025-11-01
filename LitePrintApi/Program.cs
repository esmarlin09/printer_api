// Program.cs
using LitePrintApi.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Configurar Windows Service
builder.Host.UseWindowsService();

// Configurar servicios
builder.Services.AddApplicationServices();

var app = builder.Build();

// Configurar URLs después de Build
app.Urls.Add("http://localhost:9005");

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
