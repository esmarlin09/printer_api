using System.Management;
using System.Runtime.InteropServices;
using System.Text;
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
    Task<bool> PrintInvoiceAsync(InvoiceRequest request);
    Task<bool> PrintRawAsync(string printerName, string content, string? encoding = null);
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
        string tempFilePath = "";
        
        try
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                throw new PlatformNotSupportedException("Printing is only supported on Windows");
            }

            _logger.LogInformation("🚀 INICIANDO PROCESO DE IMPRESIÓN");
            _logger.LogInformation("Impresora destino: {Printer}", printerName);
            _logger.LogInformation("Copias solicitadas: {Copies}", copies);

            // Paso 1: Decodificar base64 a bytes
            _logger.LogInformation("📄 Decodificando PDF desde base64...");
            byte[] pdfBytes;
            try
            {
                pdfBytes = Convert.FromBase64String(base64Pdf);
                _logger.LogInformation("✅ PDF decodificado correctamente. Tamaño: {Size} bytes", pdfBytes.Length);
            }
            catch (FormatException ex)
            {
                _logger.LogError(ex, "❌ Error al decodificar base64");
                throw new ArgumentException($"Formato base64 inválido: {ex.Message}", ex);
            }

            // Paso 2: Guardar en archivo temporal
            tempFilePath = Path.Combine(Path.GetTempPath(), $"print_{Guid.NewGuid()}.pdf");
            _logger.LogInformation("💾 Guardando PDF temporal en: {Path}", tempFilePath);
            await File.WriteAllBytesAsync(tempFilePath, pdfBytes);
            
            if (!File.Exists(tempFilePath))
            {
                throw new InvalidOperationException("No se pudo crear el archivo temporal PDF");
            }
            _logger.LogInformation("✅ PDF temporal guardado correctamente");

            // Paso 3: Verificar que la impresora existe
            _logger.LogInformation("🔍 Verificando existencia de impresora: {Printer}", printerName);
            bool printerFound = false;
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    $"SELECT Name FROM Win32_Printer WHERE Name = '{printerName.Replace("'", "''")}'");

                foreach (ManagementObject printerObj in searcher.Get())
                {
                    printerFound = true;
                    break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Advertencia al verificar impresora via WMI");
                // Continuar con método alternativo
            }

            if (!printerFound)
            {
                // Método alternativo: verificar en lista de impresoras instaladas
                var installedPrinters = GetPrinterNames();
                printerFound = installedPrinters.Any(p => p.Equals(printerName, StringComparison.OrdinalIgnoreCase));
            }

            if (!printerFound)
            {
                _logger.LogError("❌ Impresora no encontrada: {Printer}", printerName);
                _logger.LogInformation("Impresoras disponibles: {Printers}", string.Join(", ", GetPrinterNames()));
                throw new ArgumentException($"Impresora '{printerName}' no encontrada");
            }

            _logger.LogInformation("✅ Impresora encontrada: {Printer}", printerName);

            // Paso 4: Buscar Ghostscript
            _logger.LogInformation("🔎 Buscando Ghostscript...");
            string? gsPath = FindGhostscript();
            
            if (gsPath == null)
            {
                _logger.LogError("❌ Ghostscript no encontrado");
                _logger.LogInformation("💡 Solución: Instalar Ghostscript desde https://www.ghostscript.com/download/gsdnld.html");
                throw new InvalidOperationException("Ghostscript no encontrado. Por favor instale Ghostscript.");
            }

            _logger.LogInformation("✅ Ghostscript encontrado en: {Path}", gsPath);

            // Paso 5: Detectar tipo de impresora y configurar argumentos
            bool isPosPrinter = printerName.Contains("POS", StringComparison.OrdinalIgnoreCase) ||
                               printerName.Contains("thermal", StringComparison.OrdinalIgnoreCase) ||
                               printerName.Contains("80", StringComparison.OrdinalIgnoreCase) ||
                               printerName.Contains("ticket", StringComparison.OrdinalIgnoreCase) ||
                               printerName.Contains("termica", StringComparison.OrdinalIgnoreCase) ||
                               printerName.Contains("Epson TM", StringComparison.OrdinalIgnoreCase);

            _logger.LogInformation("📠 Tipo de impresora detectada - POS: {IsPosPrinter}", isPosPrinter);

            string gsArguments;
            if (isPosPrinter)
            {
                _logger.LogInformation("⚙️ Configurando para impresora POS de 80 columnas...");
                // Configuración optimizada para impresoras térmicas POS
                gsArguments = $"-dNOPAUSE -dBATCH " +
                              $"-dPDFFitPage " +
                              $"-dFIXEDMEDIA " +
                              $"-dDEVICEWIDTHPOINTS=576 " +  // 80 columnas
                              $"-dDEVICEHEIGHTPOINTS=9999 " + // Altura para rollo continuo
                              $"-dAutoRotatePages=/None " +
                              $"-dUseCropBox " +
                              $"-sDEVICE=mswinpr2 " +
                              $"-sOutputFile=\"%printer%{printerName}\" " +
                              $"\"{tempFilePath}\"";
            }
            else
            {
                _logger.LogInformation("⚙️ Configurando para impresora estándar...");
                // Configuración estándar
                gsArguments = $"-dNOPAUSE -dBATCH " +
                              $"-sDEVICE=mswinpr2 " +
                              $"-sOutputFile=\"%printer%{printerName}\" " +
                              $"\"{tempFilePath}\"";
            }

            _logger.LogInformation("🔧 Argumentos de Ghostscript: {Arguments}", gsArguments);

            // Paso 6: Ejecutar Ghostscript para cada copia
            _logger.LogInformation("🖨️ Iniciando proceso de impresión...");

            for (int copy = 0; copy < copies; copy++)
            {
                _logger.LogInformation("📋 Imprimiendo copia {Copy} de {Total}", copy + 1, copies);

                using var process = new System.Diagnostics.Process();
                process.StartInfo.FileName = gsPath;
                process.StartInfo.Arguments = gsArguments;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.WorkingDirectory = Path.GetTempPath();

                // Configurar manejadores de eventos para salida en tiempo real
                var outputBuilder = new StringBuilder();
                var errorBuilder = new StringBuilder();

                process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        outputBuilder.AppendLine(e.Data);
                        _logger.LogInformation("Ghostscript Output: {Data}", e.Data);
                    }
                };

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        errorBuilder.AppendLine(e.Data);
                        _logger.LogWarning("Ghostscript Error: {Data}", e.Data);
                    }
                };

                _logger.LogInformation("▶️ Iniciando proceso Ghostscript...");
                bool started = process.Start();
                
                if (!started)
                {
                    throw new InvalidOperationException("No se pudo iniciar el proceso Ghostscript");
                }

                _logger.LogInformation("✅ Proceso Ghostscript iniciado (PID: {ProcessId})", process.Id);

                // Comenzar a leer salidas asincrónicamente
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // Esperar a que termine el proceso con timeout
                var timeout = TimeSpan.FromMinutes(3);
                var processTask = process.WaitForExitAsync();
                var completedTask = await Task.WhenAny(processTask, Task.Delay(timeout));

                if (completedTask != processTask)
                {
                    _logger.LogError("⏰ Timeout: Proceso Ghostscript excedió el tiempo límite de {Timeout} minutos", timeout.TotalMinutes);
                    
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill(entireProcessTree: true);
                            _logger.LogWarning("Proceso Ghostscript terminado forzadamente");
                        }
                    }
                    catch (Exception killEx)
                    {
                        _logger.LogError(killEx, "Error al terminar proceso");
                    }

                    throw new TimeoutException($"El proceso de impresión excedió el tiempo límite de {timeout.TotalMinutes} minutos");
                }

                // Esperar un poco más para asegurar que toda la salida se capture
                await Task.Delay(1000);

                int exitCode = process.ExitCode;
                _logger.LogInformation("📝 Ghostscript finalizó con código: {ExitCode}", exitCode);

                if (exitCode != 0)
                {
                    string errorOutput = errorBuilder.ToString();
                    string standardOutput = outputBuilder.ToString();
                    
                    _logger.LogError("❌ Error en Ghostscript. Código: {ExitCode}", exitCode);
                    if (!string.IsNullOrEmpty(errorOutput))
                        _logger.LogError("Detalles del error: {Error}", errorOutput);
                    if (!string.IsNullOrEmpty(standardOutput))
                        _logger.LogError("Salida estándar: {Output}", standardOutput);

                    throw new InvalidOperationException($"Ghostscript falló con código {exitCode}. Error: {errorOutput}");
                }

                _logger.LogInformation("✅ Copia {Copy} impresa exitosamente", copy + 1);

                // Pequeña pausa entre copias
                if (copy < copies - 1)
                {
                    _logger.LogInformation("⏳ Esperando entre copias...");
                    await Task.Delay(1000);
                }
            }

            _logger.LogInformation("🎉 ¡IMPRESIÓN COMPLETADA EXITOSAMENTE!");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ ERROR CRÍTICO en el proceso de impresión");
            throw;
        }
        finally
        {
            // Limpiar archivo temporal
            if (!string.IsNullOrEmpty(tempFilePath) && File.Exists(tempFilePath))
            {
                try
                {
                    File.Delete(tempFilePath);
                    _logger.LogInformation("🧹 Archivo temporal eliminado: {Path}", tempFilePath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "No se pudo eliminar archivo temporal: {Path}", tempFilePath);
                }
            }
        }
    }

    private string? FindGhostscript()
    {
        _logger.LogInformation("Buscando Ghostscript en ubicaciones comunes...");

        var possiblePaths = new[]
        {
            // Versiones recientes 64-bit
            @"C:\Program Files\gs\gs10.03.0\bin\gswin64c.exe",
            @"C:\Program Files\gs\gs10.02.1\bin\gswin64c.exe",
            @"C:\Program Files\gs\gs10.01.2\bin\gswin64c.exe",
            @"C:\Program Files\gs\gs10.00.0\bin\gswin64c.exe",
            @"C:\Program Files\gs\gs9.56.1\bin\gswin64c.exe",
            @"C:\Program Files\gs\gs9.55.0\bin\gswin64c.exe",
            @"C:\Program Files\gs\gs9.54.0\bin\gswin64c.exe",
            @"C:\Program Files\gs\gs9.53.3\bin\gswin64c.exe",
            @"C:\Program Files\gs\gs9.52.0\bin\gswin64c.exe",
            @"C:\Program Files\gs\gs9.51.0\bin\gswin64c.exe",
            @"C:\Program Files\gs\gs9.50.0\bin\gswin64c.exe",
            
            // Versiones recientes 32-bit
            @"C:\Program Files (x86)\gs\gs10.03.0\bin\gswin32c.exe",
            @"C:\Program Files (x86)\gs\gs10.02.1\bin\gswin32c.exe",
            @"C:\Program Files (x86)\gs\gs10.01.2\bin\gswin32c.exe",
            @"C:\Program Files (x86)\gs\gs10.00.0\bin\gswin32c.exe",
            @"C:\Program Files (x86)\gs\gs9.56.1\bin\gswin32c.exe",
            @"C:\Program Files (x86)\gs\gs9.55.0\bin\gswin32c.exe",
            @"C:\Program Files (x86)\gs\gs9.54.0\bin\gswin32c.exe",
            @"C:\Program Files (x86)\gs\gs9.53.3\bin\gswin32c.exe",
            @"C:\Program Files (x86)\gs\gs9.52.0\bin\gswin32c.exe",
            @"C:\Program Files (x86)\gs\gs9.51.0\bin\gswin32c.exe",
            @"C:\Program Files (x86)\gs\gs9.50.0\bin\gswin32c.exe",
            
            // Nombres genéricos (PATH)
            "gswin64c.exe",
            "gswin32c.exe",
            "gs.exe"
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
            {
                _logger.LogInformation("✅ Ghostscript encontrado en: {Path}", path);
                return path;
            }
        }

        // Buscar usando el comando WHERE de Windows
        _logger.LogInformation("Buscando Ghostscript en PATH...");
        try
        {
            var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = "where";
            process.StartInfo.Arguments = "gswin64c";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.CreateNoWindow = true;

            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            if (!string.IsNullOrWhiteSpace(output))
            {
                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var cleanPath = line.Trim();
                    if (File.Exists(cleanPath))
                    {
                        _logger.LogInformation("✅ Ghostscript encontrado via WHERE: {Path}", cleanPath);
                        return cleanPath;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo buscar Ghostscript en PATH");
        }

        _logger.LogWarning("❌ Ghostscript no encontrado en ninguna ubicación conocida");
        return null;
    }

    /// <summary>
    /// Imprime una factura formateada en la impresora especificada (RAW)
    /// </summary>
    public async Task<bool> PrintInvoiceAsync(InvoiceRequest request)
    {
        try
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                throw new PlatformNotSupportedException("Printing is only supported on Windows");
            }

            _logger.LogInformation("🧾 INICIANDO IMPRESIÓN DE FACTURA");
            _logger.LogInformation("Impresora destino: {Printer}", request.Printer);
            _logger.LogInformation("Número de factura: {InvoiceNumber}", request.Invoice.InvoiceNumber);
            _logger.LogInformation("Cliente: {CustomerName}", request.Customer.Name);

            // Verificar que la impresora existe
            _logger.LogInformation("🔍 Verificando existencia de impresora: {Printer}", request.Printer);
            bool printerFound = GetPrinterNames().Any(p => p.Equals(request.Printer, StringComparison.OrdinalIgnoreCase));

            if (!printerFound)
            {
                _logger.LogError("❌ Impresora no encontrada: {Printer}", request.Printer);
                _logger.LogInformation("Impresoras disponibles: {Printers}", string.Join(", ", GetPrinterNames()));
                throw new ArgumentException($"Impresora '{request.Printer}' no encontrada");
            }

            _logger.LogInformation("✅ Impresora encontrada: {Printer}", request.Printer);

            // Formatear la factura
            _logger.LogInformation("📝 Formateando factura...");
            var formatter = new InvoiceFormatter();
            string formattedContent = formatter.FormatInvoice(request);
            
            _logger.LogInformation("✅ Factura formateada. Longitud: {Length} caracteres", formattedContent.Length);

            // Determinar encoding
            Encoding encoding = Encoding.UTF8;
            if (!string.IsNullOrEmpty(request.Encoding) && request.Encoding.Equals("Windows-1252", StringComparison.OrdinalIgnoreCase))
            {
                encoding = Encoding.GetEncoding(1252);
            }
            else if (!string.IsNullOrEmpty(request.Encoding) && request.Encoding.Equals("ASCII", StringComparison.OrdinalIgnoreCase))
            {
                encoding = Encoding.ASCII;
            }

            // Enviar a la impresora (RAW)
            _logger.LogInformation("🖨️ Enviando factura a la impresora (RAW)...");
            
            // Ejecutar en thread pool para no bloquear
            bool success = await Task.Run(() =>
            {
                try
                {
                    return RawPrinterHelper.SendStringToPrinter(request.Printer, formattedContent, encoding);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error al enviar datos a la impresora");
                    throw;
                }
            });

            if (success)
            {
                _logger.LogInformation("🎉 ¡FACTURA IMPRESA EXITOSAMENTE!");
                return true;
            }
            else
            {
                _logger.LogError("❌ Error al imprimir factura (retornó false)");
                throw new InvalidOperationException("Error al enviar datos a la impresora");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ ERROR CRÍTICO al imprimir factura");
            throw;
        }
    }

    /// <summary>
    /// Imprime contenido RAW directamente a la impresora
    /// </summary>
    public async Task<bool> PrintRawAsync(string printerName, string content, string? encoding = null)
    {
        try
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                throw new PlatformNotSupportedException("Printing is only supported on Windows");
            }

            _logger.LogInformation("🖨️ IMPRESIÓN RAW");
            _logger.LogInformation("Impresora destino: {Printer}", printerName);
            _logger.LogInformation("Longitud del contenido: {Length} caracteres", content.Length);

            // Verificar que la impresora existe
            bool printerFound = GetPrinterNames().Any(p => p.Equals(printerName, StringComparison.OrdinalIgnoreCase));

            if (!printerFound)
            {
                _logger.LogError("❌ Impresora no encontrada: {Printer}", printerName);
                throw new ArgumentException($"Impresora '{printerName}' no encontrada");
            }

            // Determinar encoding
            Encoding enc = Encoding.UTF8;
            if (!string.IsNullOrEmpty(encoding))
            {
                if (encoding.Equals("Windows-1252", StringComparison.OrdinalIgnoreCase))
                {
                    enc = Encoding.GetEncoding(1252);
                }
                else if (encoding.Equals("ASCII", StringComparison.OrdinalIgnoreCase))
                {
                    enc = Encoding.ASCII;
                }
            }

            // Enviar a la impresora
            bool success = await Task.Run(() =>
            {
                return RawPrinterHelper.SendStringToPrinter(printerName, content, enc);
            });

            if (success)
            {
                _logger.LogInformation("✅ Impresión RAW completada exitosamente");
                return true;
            }
            else
            {
                throw new InvalidOperationException("Error al enviar datos a la impresora");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ ERROR al imprimir RAW");
            throw;
        }
    }
}