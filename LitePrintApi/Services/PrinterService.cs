using System.Management;
using System.Runtime.InteropServices;
using LitePrintApi.Models;
using Microsoft.Extensions.Logging;

namespace LitePrintApi.Services;

public interface IPrinterService
{
    List<PrinterInfo> GetPrinters();
    List<string> GetPrinterNames();
    string GetDefaultPrinterName();
    string GetDeviceId();
    Task<bool> PrintPdfAsync(string printerName, string base64Pdf, int copies, bool removeMargins);
}

public class PrinterService : IPrinterService
{
    private readonly ILogger<PrinterService> _logger;

    public PrinterService(ILogger<PrinterService> logger)
    {
        _logger = logger;
    }
    public List<PrinterInfo> GetPrinters()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new List<PrinterInfo>();

        var printers = new List<PrinterInfo>();
        string defaultPrinter = GetDefaultPrinterName();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, Default, WorkOffline, PrinterStatus FROM Win32_Printer");

            foreach (ManagementObject p in searcher.Get())
            {
                var name = (string?)p["Name"] ?? "";
                bool isDefault = (bool?)p["Default"] ?? false;
                bool workOffline = (bool?)p["WorkOffline"] ?? false;
                var status = p["PrinterStatus"]?.ToString() ?? "Unknown";

                printers.Add(new PrinterInfo
                {
                    Name = name,
                    Default = isDefault || name.Equals(defaultPrinter, StringComparison.OrdinalIgnoreCase),
                    WorkOffline = workOffline,
                    Status = status
                });
            }
        }
        catch
        {
            // Fallback solo Windows si WMI falla
            foreach (string name in System.Drawing.Printing.PrinterSettings.InstalledPrinters)
            {
                printers.Add(new PrinterInfo
                {
                    Name = name,
                    Default = name.Equals(defaultPrinter, StringComparison.OrdinalIgnoreCase),
                    WorkOffline = null,
                    Status = null
                });
            }
        }

        return printers;
    }

    public string GetDefaultPrinterName()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return string.Empty;

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name FROM Win32_Printer WHERE Default = TRUE");

            foreach (ManagementObject p in searcher.Get())
                return (string?)p["Name"] ?? "";
        }
        catch { }

        return string.Empty;
    }

    public List<string> GetPrinterNames()
    {
        return GetPrinters().Select(p => p.Name).ToList();
    }

    public string GetDeviceId()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Environment.MachineName;

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BIOS");
            foreach (ManagementObject obj in searcher.Get())
            {
                var serial = obj["SerialNumber"]?.ToString();
                if (!string.IsNullOrWhiteSpace(serial))
                    return serial;
            }
        }
        catch { }

        // Fallback al MachineName si no se puede obtener el serial
        return Environment.MachineName;
    }

    public async Task<bool> PrintPdfAsync(string printerName, string base64Pdf, int copies, bool removeMargins)
    {
        try
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Intentar método alternativo para macOS/Linux si es necesario
                throw new PlatformNotSupportedException("Printing is only supported on Windows");
            }

            // Decodificar base64 a bytes
            _logger.LogInformation("Decodificando PDF desde base64...");
            byte[] pdfBytes;
            try
            {
                pdfBytes = Convert.FromBase64String(base64Pdf);
                _logger.LogInformation("PDF decodificado exitosamente. Tamaño: {Size} bytes", pdfBytes.Length);
            }
            catch (FormatException ex)
            {
                _logger.LogError(ex, "Error al decodificar base64: {Error}", ex.Message);
                throw new ArgumentException($"Invalid base64 PDF format: {ex.Message}", ex);
            }

            // Guardar en archivo temporal
            string tempFilePath = Path.Combine(Path.GetTempPath(), $"print_{Guid.NewGuid()}.pdf");
            _logger.LogInformation("Guardando PDF temporal en: {Path}", tempFilePath);
            await File.WriteAllBytesAsync(tempFilePath, pdfBytes);

            try
            {
                // Usar WMI para verificar que la impresora existe
                _logger.LogInformation("Verificando existencia de impresora: {Printer}", printerName);
                using var searcher = new ManagementObjectSearcher(
                    $"SELECT Name FROM Win32_Printer WHERE Name = '{printerName.Replace("'", "''")}'");

                var printerFound = false;
                foreach (ManagementObject printerObj in searcher.Get())
                {
                    printerFound = true;
                    break;
                }

                if (!printerFound)
                {
                    _logger.LogError("Impresora no encontrada: {Printer}", printerName);
                    throw new ArgumentException($"Printer '{printerName}' not found");
                }
                _logger.LogInformation("Impresora encontrada: {Printer}", printerName);

                // Imprimir usando Ghostscript
                var gsPaths = new[]
                {
                    @"C:\Program Files\gs\gs10.05.0\bin\gswin64c.exe",
                    @"C:\Program Files\gs\gs10.04.0\bin\gswin64c.exe",
                    @"C:\Program Files\gs\gs10.03.0\bin\gswin64c.exe",
                    @"C:\Program Files\gs\gs10.02.1\bin\gswin64c.exe",
                    @"C:\Program Files\gs\gs10.01.2\bin\gswin64c.exe",
                    @"C:\Program Files (x86)\gs\gs10.05.0\bin\gswin32c.exe",
                    @"C:\Program Files (x86)\gs\gs10.04.0\bin\gswin32c.exe",
                    @"C:\Program Files (x86)\gs\gs10.03.0\bin\gswin32c.exe",
                    @"C:\Program Files (x86)\gs\gs10.02.1\bin\gswin32c.exe",
                    @"C:\Program Files (x86)\gs\gs10.01.2\bin\gswin32c.exe"
                };

                string? gsPath = null;
                _logger.LogInformation("Buscando instalación de Ghostscript...");
                foreach (var path in gsPaths)
                {
                    if (File.Exists(path))
                    {
                        gsPath = path;
                        _logger.LogInformation("Ghostscript encontrado en: {Path}", gsPath);
                        break;
                    }
                }

                if (gsPath == null)
                {
                    _logger.LogError("Ghostscript no encontrado en ninguna de las rutas conocidas");
                    throw new InvalidOperationException("Ghostscript not found. Please install Ghostscript (https://www.ghostscript.com/download/gsdnld.html)");
                }

                // Detectar si es una impresora PDF virtual que requiere diálogo
                bool isPdfVirtualPrinter = printerName.Contains("Print to PDF", StringComparison.OrdinalIgnoreCase) ||
                                          printerName.Contains("PDF", StringComparison.OrdinalIgnoreCase) &&
                                          printerName.Contains("Microsoft", StringComparison.OrdinalIgnoreCase);

                if (isPdfVirtualPrinter)
                {
                    _logger.LogWarning("ADVERTENCIA: La impresora '{Printer}' es una impresora PDF virtual que requiere diálogos interactivos. " +
                        "Como el servicio se ejecuta sin interfaz de usuario, esta impresora no funcionará correctamente. " +
                        "Recomendamos usar una impresora física o configurar la impresora PDF para modo silencioso.", printerName);

                    // Intentar usar método alternativo con argumentos para evitar diálogos
                    _logger.LogInformation("Intentando impresión con método alternativo para evitar diálogos...");
                }

                // Imprimir cada copia
                for (int copy = 0; copy < copies; copy++)
                {
                    _logger.LogInformation("Iniciando impresión copia {Copy} de {Total}", copy + 1, copies);

                    // Para impresoras PDF virtuales, usar argumentos adicionales para evitar diálogos
                    string gsArguments;
                    if (isPdfVirtualPrinter)
                    {
                        // Intentar con argumentos que eviten diálogos (puede no funcionar para todas las impresoras PDF)
                        gsArguments = $"-dNOPAUSE -dBATCH -dNoCancel -sDEVICE=mswinpr2 -sOutputFile=\"%printer%{printerName}\" \"{tempFilePath}\"";
                        _logger.LogWarning("Usando argumentos especiales para impresora PDF virtual. Si falla, use una impresora física.");
                    }
                    else
                    {
                        gsArguments = $"-dNOPAUSE -dBATCH -sDEVICE=mswinpr2 -sOutputFile=\"%printer%{printerName}\" \"{tempFilePath}\"";
                    }

                    // Detectar si estamos ejecutándonos como servicio
                    bool isRunningAsService = Environment.UserInteractive == false;

                    System.Diagnostics.Process? process;

                    if (isRunningAsService)
                    {
                        _logger.LogInformation("Ejecutándose como servicio de Windows. Intentando ejecutar en sesión del usuario interactivo...");

                        // Para servicios, intentar ejecutar en la sesión del usuario interactivo
                        // Esto es especialmente importante para impresoras PDF virtuales
                        bool showWindow = isPdfVirtualPrinter; // Mostrar ventana solo para PDF virtuales
                        process = InteractiveProcessHelper.StartProcessInInteractiveSession(
                            gsPath,
                            gsArguments,
                            _logger,
                            showWindow);

                        if (process == null)
                        {
                            _logger.LogWarning("No se pudo ejecutar en sesión interactiva, intentando método normal...");
                            // Fallback a método normal
                            var processInfo = new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = gsPath,
                                Arguments = gsArguments,
                                UseShellExecute = true,
                                CreateNoWindow = !isPdfVirtualPrinter,
                                WindowStyle = isPdfVirtualPrinter ?
                                    System.Diagnostics.ProcessWindowStyle.Normal :
                                    System.Diagnostics.ProcessWindowStyle.Hidden
                            };
                            process = System.Diagnostics.Process.Start(processInfo);
                        }
                    }
                    else
                    {
                        // Ejecución normal (no servicio)
                        var processInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = gsPath,
                            Arguments = gsArguments,
                            UseShellExecute = false,
                            CreateNoWindow = !isPdfVirtualPrinter,
                            WindowStyle = isPdfVirtualPrinter ?
                                System.Diagnostics.ProcessWindowStyle.Normal :
                                System.Diagnostics.ProcessWindowStyle.Hidden,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            RedirectStandardInput = false
                        };
                        process = System.Diagnostics.Process.Start(processInfo);
                    }

                    if (process == null)
                    {
                        _logger.LogError("No se pudo iniciar el proceso de Ghostscript");
                        throw new InvalidOperationException("Failed to start Ghostscript process");
                    }

                    _logger.LogInformation("Proceso Ghostscript iniciado (PID: {ProcessId}). Comando: {FileName} {Arguments}",
                        process.Id, gsPath, gsArguments);

                    // Usar variable para evitar error en caso de que processInfo no exista
                    using var processDisposable = process;

                    // Leer salida solo si no es UseShellExecute
                    Task<string>? outputTask = null;
                    Task<string>? errorTask = null;

                    try
                    {
                        if (process.StartInfo.RedirectStandardOutput)
                        {
                            outputTask = process.StandardOutput.ReadToEndAsync();
                            errorTask = process.StandardError.ReadToEndAsync();
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        // Si no se puede redirigir (UseShellExecute = true), continuar sin capturar salida
                        _logger.LogInformation("No se puede capturar salida del proceso (UseShellExecute activado)");
                    }

                    // Monitorear el proceso con logging periódico
                    var processStartTime = DateTime.Now;
                    var monitoringTask = Task.Run(async () =>
                    {
                        while (!process.HasExited)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(5));
                            if (!process.HasExited)
                            {
                                var elapsed = (DateTime.Now - processStartTime).TotalSeconds;
                                _logger.LogInformation("Proceso Ghostscript aún ejecutándose (PID: {ProcessId}, Tiempo transcurrido: {Seconds}s)",
                                    process.Id, elapsed);
                            }
                        }
                    });

                    // Esperar a que termine el proceso con un timeout más corto para PDF virtuales
                    var timeout = isPdfVirtualPrinter ? TimeSpan.FromSeconds(30) : TimeSpan.FromMinutes(2);
                    _logger.LogInformation("Timeout configurado: {Timeout} segundos", timeout.TotalSeconds);
                    var processTask = process.WaitForExitAsync();
                    var startTime = DateTime.Now;
                    var completedTask = await Task.WhenAny(processTask, Task.Delay(timeout));

                    var elapsed = (DateTime.Now - startTime).TotalSeconds;
                    _logger.LogInformation("Espera completada después de {Seconds} segundos", elapsed);

                    if (completedTask == processTask)
                    {
                        _logger.LogInformation("Proceso terminó normalmente");
                        if (outputTask != null && errorTask != null)
                        {
                            _logger.LogInformation("Esperando captura de salida...");
                            await Task.WhenAll(outputTask, errorTask);
                            _logger.LogInformation("Salida capturada completamente");
                        }
                    }
                    else
                    {
                        _logger.LogError("Proceso de Ghostscript excedió el timeout de {Timeout} segundos. Elapsed: {Seconds}s",
                            timeout.TotalSeconds, elapsed);

                        // Verificar estado del proceso
                        try
                        {
                            if (!process.HasExited)
                            {
                                _logger.LogWarning("Proceso aún corriendo, intentando terminar...");
                                process.Kill(entireProcessTree: true);
                                await Task.Delay(1000); // Esperar un segundo para que termine
                                _logger.LogWarning("Proceso Ghostscript terminado forzadamente");
                            }
                            else
                            {
                                _logger.LogInformation("Proceso ya había terminado antes del timeout");
                            }
                        }
                        catch (Exception killEx)
                        {
                            _logger.LogError(killEx, "Error al terminar proceso: {Error}", killEx.Message);
                        }

                        string errorMsg = $"Ghostscript process exceeded timeout of {timeout.TotalSeconds} seconds";
                        if (isPdfVirtualPrinter)
                        {
                            errorMsg += ". NOTA: Las impresoras PDF virtuales como 'Microsoft Print to PDF' requieren diálogos interactivos " +
                                       "que no están disponibles cuando el servicio se ejecuta sin interfaz de usuario. " +
                                       "Use una impresora física o configure la impresora PDF para modo automático.";
                        }

                        throw new TimeoutException(errorMsg);
                    }

                    string output = outputTask != null ? await outputTask : string.Empty;
                    string error = errorTask != null ? await errorTask : string.Empty;
                    int exitCode = process.ExitCode;

                    _logger.LogInformation("Ghostscript terminó con código de salida: {ExitCode}", exitCode);

                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        _logger.LogInformation("Salida de Ghostscript: {Output}", output);
                    }

                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        _logger.LogWarning("Error de Ghostscript: {Error}", error);
                    }

                    if (exitCode != 0)
                    {
                        string errorMessage = $"Ghostscript failed with exit code {exitCode}";
                        if (!string.IsNullOrWhiteSpace(error))
                            errorMessage += $". Error: {error}";
                        if (!string.IsNullOrWhiteSpace(output))
                            errorMessage += $". Output: {output}";

                        _logger.LogError("Error en impresión: {Error}", errorMessage);
                        throw new InvalidOperationException(errorMessage);
                    }

                    _logger.LogInformation("✅ Copia {Copy} impresa exitosamente", copy + 1);

                    if (copy < copies - 1)
                        await Task.Delay(1000);
                }

                return true;
            }
            finally
            {
                // Intentar eliminar el archivo temporal después de un pequeño retraso
                _ = Task.Run(async () =>
                {
                    await Task.Delay(5000);
                    try { File.Delete(tempFilePath); } catch { }
                });
            }
        }
        catch (Exception)
        {
            // Propagar la excepción para que el endpoint pueda capturarla y devolver el mensaje
            throw;
        }
    }
}

