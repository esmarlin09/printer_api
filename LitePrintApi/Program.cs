// Program.cs
using LitePrintApi.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Configurar Windows Service PRIMERO
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

app.Run();
