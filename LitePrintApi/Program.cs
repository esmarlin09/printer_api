// Program.cs
using LitePrintApi.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Configurar Windows Service
builder.Host.UseWindowsService();

// Configurar URLs por defecto para el servicio
builder.WebHost.UseUrls("http://localhost:5000");

// Configurar servicios
builder.Services.AddApplicationServices();

var app = builder.Build();

// Configurar middleware
app.UseApplicationMiddleware();

// Configurar endpoints
app.MapApplicationEndpoints();

app.Run();
