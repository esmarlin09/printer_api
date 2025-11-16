using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Drawing.Printing;
using LitePrintApi.Models;
using LitePrintApi.Services;
using Serilog;
using Serilog.Events;
using System.Runtime.InteropServices;
using System.Windows.Forms;

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

// ✅ Servicios básicos + Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 🔓 CORS: PERMITIR ABSOLUTAMENTE TODOS LOS ORÍGENES SIN RESTRICCIONES
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()  // ✅ Cualquier origen
              .AllowAnyMethod()  // ✅ Cualquier método (GET, POST, etc.)
              .AllowAnyHeader()  // ✅ Cualquier header
              .WithExposedHeaders("*"); // ✅ Exponer todos los headers
    });

    // Política adicional por si acaso
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader()
              .WithExposedHeaders("*")
              .SetPreflightMaxAge(TimeSpan.FromHours(24)); // ✅ Cache preflight
    });
});

// ✅ Registrar PrinterService
builder.Services.AddScoped<IPrinterService, PrinterService>();

// ✅ Warmup no bloqueante
builder.Services.AddHostedService<WarmupService>();

// ✅ Registrar el servicio del system tray
builder.Services.AddSingleton<TrayService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<TrayService>());

var app = builder.Build();

// ✅ IMPORTANTE: Aplicar CORS al inicio de la pipeline
app.UseCors(); // ✅ Usa la política por defecto
app.UseCors("AllowAll"); // ✅ Y también la política específica

// Manejar preflight requests globalmente
app.Use(async (context, next) =>
{
    if (context.Request.Method == "OPTIONS")
    {
        context.Response.Headers.Add("Access-Control-Allow-Origin", "*");
        context.Response.Headers.Add("Access-Control-Allow-Methods", "*");
        context.Response.Headers.Add("Access-Control-Allow-Headers", "*");
        context.Response.Headers.Add("Access-Control-Max-Age", "86400");
        context.Response.StatusCode = 200;
        await context.Response.CompleteAsync();
        return;
    }
    await next();
});

// ✅ Swagger UI
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "LitePrint API V1");
    c.RoutePrefix = "swagger";
});

// ✅ Endpoints simples - TODOS permiten CORS automáticamente
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

        var logFiles = Directory.GetFiles(logDirectory, "liteprint-*.log")
            .OrderByDescending(f => File.GetCreationTime(f))
            .ToList();

        if (logFiles.Count == 0)
        {
            return Results.Ok(new { logs = new List<string>(), message = "No hay archivos de log" });
        }

        var latestLogFile = logFiles.First();
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

// ✅ Endpoint de impresión con CORS explícito
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

        logger.LogInformation("Iniciando impresión: Printer={Printer}, Copies={Copies}", request.Printer, request.Copies);

        _ = Task.Run(async () =>
        {
            try
            {
                logger.LogInformation("Ejecutando impresión en segundo plano...");
                await printerService.PrintPdfAsync(request.Printer, request.Base64Pdf, request.Copies, request.RemoveMargins);
                logger.LogInformation("✅ Impresión completada exitosamente en segundo plano");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "❌ ERROR en impresión en segundo plano: {Error}", ex.Message);
            }
        });

        return Results.Ok(new { message = "Print job queued successfully" });
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = "Internal server error", details = ex.Message }, statusCode: 500);
    }
}).WithTags("Print");

// ✅ Endpoint de prueba CORS
app.MapGet("/cors-test", () => 
{
    return Results.Ok(new { 
        message = "CORS está funcionando correctamente", 
        timestamp = DateTime.Now,
        cors = "TODOS los orígenes permitidos",
        methods = "TODOS los métodos permitidos",
        headers = "TODOS los headers permitidos"
    });
}).WithTags("System");

// ✅ Endpoint OPTIONS global para preflight
app.MapMethods("/print", new[] { "OPTIONS" }, () =>
{
    return Results.Ok();
}).WithTags("Print");

app.MapMethods("/{*path}", new[] { "OPTIONS" }, (string path) =>
{
    return Results.Ok();
}).WithTags("System");

try
{
    Log.Information("Iniciando LitePrint API Service");
    
    // Ocultar la ventana de consola inmediatamente
    var consoleHandle = NativeMethods.GetConsoleWindow();
    if (consoleHandle != IntPtr.Zero)
    {
        NativeMethods.ShowWindow(consoleHandle, NativeMethods.SW_HIDE);
    }
    
    // Iniciar la aplicación
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

// ✅ Clase estática para las APIs de Windows
public static class NativeMethods
{
    [DllImport("kernel32.dll")]
    public static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    public const int SW_HIDE = 0;
    public const int SW_SHOW = 5;
}

// ✅ Servicio para el icono en el system tray
public class TrayService : IHostedService, IDisposable
{
    private readonly ILogger<TrayService> _logger;
    private NotifyIcon? _trayIcon;
    private IHostApplicationLifetime? _appLifetime;

    public TrayService(ILogger<TrayService> logger, IHostApplicationLifetime appLifetime)
    {
        _logger = logger;
        _appLifetime = appLifetime;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Thread trayThread = new Thread(() =>
        {
            try
            {
                _trayIcon = new NotifyIcon();
                _trayIcon.Icon = SystemIcons.Application;
                _trayIcon.Text = "LitePrint API Service\nhttp://localhost:9005";
                _trayIcon.Visible = true;

                var contextMenu = new ContextMenuStrip();
                
                var openSwaggerItem = new ToolStripMenuItem("Abrir Swagger UI");
                openSwaggerItem.Click += (sender, e) =>
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "http://localhost:9005/swagger",
                        UseShellExecute = true
                    });
                };

                var openLogsItem = new ToolStripMenuItem("Ver Logs");
                openLogsItem.Click += (sender, e) =>
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "http://localhost:9005/logs",
                        UseShellExecute = true
                    });
                };

                var showConsoleItem = new ToolStripMenuItem("Mostrar Consola");
                showConsoleItem.Click += (sender, e) =>
                {
                    var handle = NativeMethods.GetConsoleWindow();
                    if (handle != IntPtr.Zero)
                    {
                        NativeMethods.ShowWindow(handle, NativeMethods.SW_SHOW);
                    }
                };

                var hideConsoleItem = new ToolStripMenuItem("Ocultar Consola");
                hideConsoleItem.Click += (sender, e) =>
                {
                    var handle = NativeMethods.GetConsoleWindow();
                    if (handle != IntPtr.Zero)
                    {
                        NativeMethods.ShowWindow(handle, NativeMethods.SW_HIDE);
                    }
                };

                var exitItem = new ToolStripMenuItem("Salir");
                exitItem.Click += (sender, e) =>
                {
                    _logger.LogInformation("Cerrando aplicación desde el system tray...");
                    if (_trayIcon != null)
                    {
                        _trayIcon.Visible = false;
                    }
                    _appLifetime?.StopApplication();
                };

                contextMenu.Items.Add(openSwaggerItem);
                contextMenu.Items.Add(openLogsItem);
                contextMenu.Items.Add(new ToolStripSeparator());
                contextMenu.Items.Add(showConsoleItem);
                contextMenu.Items.Add(hideConsoleItem);
                contextMenu.Items.Add(new ToolStripSeparator());
                contextMenu.Items.Add(exitItem);

                _trayIcon.ContextMenuStrip = contextMenu;

                _trayIcon.DoubleClick += (sender, e) =>
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "http://localhost:9005/swagger",
                        UseShellExecute = true
                    });
                };

                _trayIcon.ShowBalloonTip(3000, "LitePrint Service", 
                    "Servicio iniciado correctamente\nhttp://localhost:9005", 
                    ToolTipIcon.Info);

                _logger.LogInformation("Icono del system tray creado correctamente");
                Application.Run();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear el icono del system tray");
            }
        });

        trayThread.SetApartmentState(ApartmentState.STA);
        trayThread.IsBackground = true;
        trayThread.Start();

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
        }
        _logger.LogInformation("Icono del system tray eliminado");
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_trayIcon != null)
        {
            _trayIcon.Dispose();
        }
    }
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
                Thread.Sleep(200);
                _logger.LogInformation("LitePrintService iniciado correctamente.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error durante la inicialización.");
            }
        }, cancellationToken);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("LitePrintService detenido.");
        return Task.CompletedTask;
    }
}