// Program.cs
using LitePrintApi.Extensions;

var builder = WebApplication.CreateBuilder(args);

// ❌ NO usar Windows Service
// builder.Host.UseWindowsService(); // <- Eliminado

// Configurar servicios
builder.Services.AddApplicationServices();

var app = builder.Build();

// Configurar middleware
app.UseApplicationMiddleware();

// Configurar endpoints
app.MapApplicationEndpoints();

app.Run();
