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
using System.Management;

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

// ✅ Configurar Kestrel / URL antes del Build (SOLO HTTP)
builder.WebHost.UseKestrel().UseUrls("http://0.0.0.0:9005");

// ✅ Servicios básicos + Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 🔓 CORS: CONFIGURACIÓN ESPECÍFICA PARA EL DOMINIO
builder.Services.AddCors(options =>
{
    options.AddPolicy("LightTechnologyPolicy", policy =>
    {
        policy.WithOrigins(
                "https://lm-pos.lighttechnology.online",
                "https://www.lm-pos.lighttechnology.online"
            )
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials()
            .WithExposedHeaders("*")
            .SetPreflightMaxAge(TimeSpan.FromHours(24));
    });

    options.AddDefaultPolicy(policy =>
    {
        policy.SetIsOriginAllowed(origin => true)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials()
              .WithExposedHeaders("*")
              .SetPreflightMaxAge(TimeSpan.FromHours(24));
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

// ✅ MIDDLEWARE CORS MEJORADO
app.UseCors("LightTechnologyPolicy");
app.UseCors();

// Middleware personalizado para headers CORS adicionales
app.Use(async (context, next) =>
{
    var origin = context.Request.Headers["Origin"].ToString();
    
    // Permitir el dominio específico
    if (origin.Contains("lighttechnology.online"))
    {
        context.Response.Headers.Append("Access-Control-Allow-Origin", origin);
        context.Response.Headers.Append("Access-Control-Allow-Credentials", "true");
    }
    else
    {
        context.Response.Headers.Append("Access-Control-Allow-Origin", "*");
    }

    context.Response.Headers.Append("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
    context.Response.Headers.Append("Access-Control-Allow-Headers", "Content-Type, Authorization, Accept, X-Requested-With, Origin");
    context.Response.Headers.Append("Access-Control-Max-Age", "86400");
    context.Response.Headers.Append("Access-Control-Expose-Headers", "*");

    // Manejar requests OPTIONS (preflight)
    if (context.Request.Method == "OPTIONS")
    {
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
    // Obtener lista de impresoras
    var printers = new List<string>();
    foreach (string printer in PrinterSettings.InstalledPrinters)
        printers.Add(printer);

    // Obtener un identificador único de la máquina (Windows)
    string deviceId = GetDeviceId();

    var result = new
    {
        deviceId = deviceId,
        printers = printers
    };

    return Results.Ok(result);
}).WithTags("Printers");

// ✅ Endpoint de impresión
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

static string GetDeviceId()
{
    try
    {
        using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystemProduct");
        foreach (ManagementObject obj in searcher.Get())
        {
            var uuid = obj["UUID"]?.ToString();
            if (!string.IsNullOrEmpty(uuid))
                return uuid;
        }
    }
    catch
    {
        // Si falla, usa un fallback basado en nombre de equipo
        return Environment.MachineName.GetHashCode().ToString();
    }

    // Fallback final
    return Environment.MachineName.GetHashCode().ToString();
}

// ✅ Endpoint de prueba CORS mejorado
app.MapGet("/cors-test", (HttpContext context) => 
{
    var origin = context.Request.Headers["Origin"].ToString();
    var userAgent = context.Request.Headers["User-Agent"].ToString();
    
    return Results.Ok(new { 
        message = "CORS está funcionando correctamente", 
        timestamp = DateTime.Now,
        yourOrigin = origin,
        allowedOrigins = new[] {
            "https://lm-pos.lighttechnology.online",
            "https://www.lm-pos.lighttechnology.online"
        },
        userAgent = userAgent,
        corsStatus = "ACTIVE",
        serverUrl = "http://localhost:9005",
        note = "Para sitios HTTPS, considera usar un proxy o servicio intermedio"
    });
}).WithTags("System");

// ✅ Endpoints OPTICS explícitos
app.MapMethods("/print", new[] { "OPTIONS" }, () =>
{
    return Results.Ok(new { message = "Preflight OK for /print" });
}).WithTags("Print");

app.MapMethods("/printers", new[] { "OPTIONS" }, () =>
{
    return Results.Ok(new { message = "Preflight OK for /printers" });
}).WithTags("Printers");

app.MapMethods("/{*path}", new[] { "OPTIONS" }, (string path) =>
{
    return Results.Ok(new { message = $"Preflight OK for path: {path}" });
}).WithTags("System");

try
{
    Log.Information("Iniciando LitePrint API Service");
    Log.Information("Servidor escuchando en: http://localhost:9005");
    Log.Information("CORS configurado para: https://lm-pos.lighttechnology.online");
    
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