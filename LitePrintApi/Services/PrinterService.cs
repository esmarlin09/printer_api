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

                // Imprimir cada copia
                for (int copy = 0; copy < copies; copy++)
                {
                    _logger.LogInformation("Iniciando impresión copia {Copy} de {Total}", copy + 1, copies);
                    
                    var processInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = gsPath,
                        Arguments = $"-dNOPAUSE -dBATCH -sDEVICE=mswinpr2 -sOutputFile=\"%printer%{printerName}\" \"{tempFilePath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    using var process = System.Diagnostics.Process.Start(processInfo);
                    if (process == null)
                    {
                        _logger.LogError("No se pudo iniciar el proceso de Ghostscript");
                        throw new InvalidOperationException("Failed to start Ghostscript process");
                    }

                    // Capturar salida estándar y error
                    string output = await process.StandardOutput.ReadToEndAsync();
                    string error = await process.StandardError.ReadToEndAsync();

                    await process.WaitForExitAsync();

                    int exitCode = process.ExitCode;
                    _logger.LogInformation("Ghostscript terminó con código de salida: {ExitCode}", exitCode);

                    if (!string.IsNullOrWhiteSpace(output))
                        _logger.LogInformation("Salida de Ghostscript: {Output}", output);

                    if (!string.IsNullOrWhiteSpace(error))
                        _logger.LogWarning("Error de Ghostscript: {Error}", error);

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

                    _logger.LogInformation("Copia {Copy} impresa exitosamente", copy + 1);

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

