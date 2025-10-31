// Program.cs
using System.Runtime.InteropServices;
using System.Management; // Solo se usa en Windows
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// ❌ NO usar Windows Service
// builder.Host.UseWindowsService(); // <- Eliminado

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .AllowAnyOrigin()
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

app.UseCors();
app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok", time = DateTime.Now }));

app.MapGet("/printers", () =>
{
    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        return Results.StatusCode(StatusCodes.Status501NotImplemented);

    var list = new List<object>();
    string defaultPrinter = GetDefaultPrinterNameWindows();

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

            list.Add(new
            {
                name,
                @default = isDefault || name.Equals(defaultPrinter, StringComparison.OrdinalIgnoreCase),
                workOffline,
                status
            });
        }
    }
    catch
    {
        // Fallback solo Windows si WMI falla
        foreach (string name in System.Drawing.Printing.PrinterSettings.InstalledPrinters)
        {
            list.Add(new
            {
                name,
                @default = name.Equals(defaultPrinter, StringComparison.OrdinalIgnoreCase),
                workOffline = (bool?)null,
                status = (string?)null
            });
        }
    }

    return Results.Ok(list);
});

app.Run();

// Helpers (solo Windows)
static string GetDefaultPrinterNameWindows()
{
    try
    {
        using var searcher = new ManagementObjectSearcher(
            "SELECT Name FROM Win32_Printer WHERE Default = TRUE");
        foreach (ManagementObject p in searcher.Get())
            return (string?)p["Name"] ?? "";
    }
    catch { }
    return "";
}
