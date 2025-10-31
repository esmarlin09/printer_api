using System.Management;
using System.Runtime.InteropServices;
using LitePrintApi.Models;

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
            byte[] pdfBytes = Convert.FromBase64String(base64Pdf);

            // Guardar en archivo temporal
            string tempFilePath = Path.Combine(Path.GetTempPath(), $"print_{Guid.NewGuid()}.pdf");
            await File.WriteAllBytesAsync(tempFilePath, pdfBytes);

            try
            {
                // Usar WMI para verificar que la impresora existe
                using var searcher = new ManagementObjectSearcher(
                    $"SELECT Name FROM Win32_Printer WHERE Name = '{printerName.Replace("'", "''")}'");

                var printerFound = false;
                foreach (ManagementObject printer in searcher.Get())
                {
                    printerFound = true;
                    break;
                }

                if (!printerFound)
                    throw new ArgumentException($"Printer '{printerName}' not found");

                // Usar Adobe Reader o el visor PDF predeterminado para imprimir
                for (int copy = 0; copy < copies; copy++)
                {
                    var processInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = tempFilePath,
                        Verb = "PrintTo",
                        Arguments = $"\"{printerName}\"",
                        UseShellExecute = true,
                        WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                    };

                    using var process = System.Diagnostics.Process.Start(processInfo);

                    if (copy < copies - 1)
                        await Task.Delay(1000); // Esperar entre copias
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

