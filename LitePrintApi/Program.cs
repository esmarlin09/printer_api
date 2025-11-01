using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Drawing.Printing;
using LitePrintApi.Models;
using LitePrintApi.Services;

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

// ✅ Registrar PrinterService
builder.Services.AddScoped<IPrinterService, PrinterService>();

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

app.MapPost("/print", (PrintRequest request, IPrinterService printerService, ILogger<Program> logger) =>
{
    try
    {
        if (string.IsNullOrWhiteSpace(request.Base64Pdf))
            return Results.BadRequest(new { error = "Base64Pdf is required" });

        if (string.IsNullOrWhiteSpace(request.Printer))
            return Results.BadRequest(new { error = "Printer is required" });

        if (request.Copies < 1)
            return Results.BadRequest(new { error = "Copies must be at least 1" });

        // Iniciar impresión en segundo plano sin bloquear
        _ = Task.Run(async () =>
        {
            try
            {
                await printerService.PrintPdfAsync(request.Printer, request.Base64Pdf, request.Copies, request.RemoveMargins);
            }
            catch (Exception ex)
            {
                // Log del error pero no bloquear la respuesta
                logger.LogError(ex, "Error en impresión en segundo plano");
            }
        });

        return Results.Ok(new { message = "Print job queued successfully" });
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = "Internal server error", details = ex.Message }, statusCode: 500);
    }
}).WithTags("Print");

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
