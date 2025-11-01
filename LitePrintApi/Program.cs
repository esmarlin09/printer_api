using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Drawing.Printing;

var builder = WebApplication.CreateBuilder(args);

// ✅ Ejecutar como servicio de Windows y asegurar ruta correcta
builder.Host.UseWindowsService();
builder.Host.UseContentRoot(AppContext.BaseDirectory);

// ✅ Logging en Event Viewer
builder.Logging.ClearProviders();
builder.Logging.AddEventLog(settings => settings.SourceName = "LitePrintService");

// ✅ Configurar Kestrel / URL antes del Build
builder.WebHost.UseKestrel().UseUrls("http://0.0.0.0:9005");

// ✅ Servicios básicos + CORS + Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 🔓 CORS: permitir todos los orígenes
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// ✅ Warmup no bloqueante
builder.Services.AddHostedService<WarmupService>();

var app = builder.Build();

// ✅ Usar CORS globalmente
app.UseCors("AllowAll");

// ✅ Swagger UI
app.UseSwagger();
app.UseSwaggerUI();

// ✅ Endpoints simples
app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTimeOffset.Now }))
   .WithTags("System");

app.MapGet("/printers", () =>
{
    var printers = new List<string>();
    foreach (string printer in PrinterSettings.InstalledPrinters)
        printers.Add(printer);
    return Results.Ok(printers);
}).WithTags("Printers");

app.Run();

// ✅ HostedService para tareas no bloqueantes al iniciar
public sealed class WarmupService : IHostedService
{
    private readonly ILogger<WarmupService> _logger;
    public WarmupService(ILogger<WarmupService> logger) => _logger = logger;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = Task.Run(() =>
        {
            try
            {
                _logger.LogInformation("Inicializando LitePrintService...");
                Thread.Sleep(200); // simulación ligera
                _logger.LogInformation("LitePrintService iniciado correctamente.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error durante la inicialización.");
            }
        }, cancellationToken);

        return Task.CompletedTask; // ⚡ Responder rápido al SCM
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("LitePrintService detenido.");
        return Task.CompletedTask;
    }
}
