using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Drawing.Printing;
using LitePrintApi.Models;
using LitePrintApi.Services;
using Serilog;
using Serilog.Events;

// ✅ Configurar Serilog para logging a archivo
var logPath = Path.Combine(AppContext.BaseDirectory, "logs", "liteprint-{Date}.log");
Log.Logger = new LoggerConfiguration()
    .WriteTo.File(
        path: logPath,
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] {Message:lj}{NewLine}{Exception}",
        shared: true
    )
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);

// ✅ Ejecutar como servicio de Windows y asegurar ruta correcta
builder.Host.UseWindowsService();
builder.Host.UseContentRoot(AppContext.BaseDirectory);

// ✅ Usar Serilog para logging
builder.Host.UseSerilog();

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

app.MapGet("/logs", (ILogger<Program> logger) =>
{
    try
    {
        var logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
        if (!Directory.Exists(logDirectory))
        {
            return Results.Ok(new { logs = new List<string>(), message = "No hay logs aún" });
        }

        // Obtener el archivo de log más reciente
        var logFiles = Directory.GetFiles(logDirectory, "liteprint-*.log")
            .OrderByDescending(f => File.GetCreationTime(f))
            .ToList();

        if (logFiles.Count == 0)
        {
            return Results.Ok(new { logs = new List<string>(), message = "No hay archivos de log" });
        }

        var latestLogFile = logFiles.First();
        var logLines = new List<string>();

        // Leer las últimas 500 líneas del archivo (o todas si son menos)
        var allLines = File.ReadAllLines(latestLogFile);
        var linesToReturn = allLines.Length > 500
            ? allLines.Skip(allLines.Length - 500).ToArray()
            : allLines;

        return Results.Ok(new
        {
            file = Path.GetFileName(latestLogFile),
            totalLines = allLines.Length,
            showingLines = linesToReturn.Length,
            logs = linesToReturn,
            lastUpdated = File.GetLastWriteTime(latestLogFile)
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error al leer logs: {Error}", ex.Message);
        return Results.Json(new { error = "Error al leer logs", details = ex.Message }, statusCode: 500);
    }
}).WithTags("System");

app.MapGet("/logs/clear", (ILogger<Program> logger) =>
{
    try
    {
        var logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
        if (Directory.Exists(logDirectory))
        {
            var logFiles = Directory.GetFiles(logDirectory, "liteprint-*.log");
            foreach (var file in logFiles)
            {
                File.Delete(file);
            }
            logger.LogInformation("Logs eliminados");
            return Results.Ok(new { message = "Logs eliminados correctamente", deletedFiles = logFiles.Length });
        }
        return Results.Ok(new { message = "No hay logs para eliminar" });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error al eliminar logs: {Error}", ex.Message);
        return Results.Json(new { error = "Error al eliminar logs", details = ex.Message }, statusCode: 500);
    }
}).WithTags("System");

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
        logger.LogInformation("Iniciando impresión: Printer={Printer}, Copies={Copies}", request.Printer, request.Copies);

        _ = Task.Run(async () =>
        {
            try
            {
                logger.LogInformation("Ejecutando impresión en segundo plano...");
                logger.LogInformation("Detalles: Printer={Printer}, Copies={Copies}, RemoveMargins={RemoveMargins}, PDFSize={Size} bytes",
                    request.Printer, request.Copies, request.RemoveMargins,
                    string.IsNullOrWhiteSpace(request.Base64Pdf) ? 0 : request.Base64Pdf.Length);

                await printerService.PrintPdfAsync(request.Printer, request.Base64Pdf, request.Copies, request.RemoveMargins);

                logger.LogInformation("✅ Impresión completada exitosamente en segundo plano");
            }
            catch (Exception ex)
            {
                // Log detallado del error
                logger.LogError(ex, "❌ ERROR en impresión en segundo plano: {Error}", ex.Message);
                logger.LogError("StackTrace: {StackTrace}", ex.StackTrace);
                if (ex.InnerException != null)
                {
                    logger.LogError("InnerException: {InnerError}", ex.InnerException.Message);
                }
            }
        });

        return Results.Ok(new { message = "Print job queued successfully" });
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = "Internal server error", details = ex.Message }, statusCode: 500);
    }
}).WithTags("Print");

try
{
    Log.Information("Iniciando LitePrint API Service");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Error fatal al iniciar el servicio");
}
finally
{
    Log.CloseAndFlush();
}

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
