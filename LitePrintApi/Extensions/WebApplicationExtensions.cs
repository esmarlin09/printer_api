using LitePrintApi.Models;
using LitePrintApi.Services;

namespace LitePrintApi.Extensions;

public static class WebApplicationExtensions
{
    public static WebApplication MapApplicationEndpoints(this WebApplication app)
    {
        app.MapGet("/healthz", () => Results.Ok(new { status = "ok", time = DateTime.Now }));

        app.MapGet("/printers", (IPrinterService printerService) =>
        {
            var deviceId = printerService.GetDeviceId();
            var printers = printerService.GetPrinterNames();

            return Results.Ok(new { deviceId, printers });
        });

        app.MapPost("/print", async (PrintRequest request, IPrinterService printerService) =>
        {
            if (string.IsNullOrWhiteSpace(request.Base64Pdf))
                return Results.BadRequest(new { error = "Base64Pdf is required" });

            if (string.IsNullOrWhiteSpace(request.Printer))
                return Results.BadRequest(new { error = "Printer is required" });

            if (request.Copies < 1)
                return Results.BadRequest(new { error = "Copies must be at least 1" });

            bool success = await printerService.PrintPdfAsync(request.Printer, request.Base64Pdf, request.Copies, request.RemoveMargins);

            if (!success)
                return Results.StatusCode(500);

            return Results.Ok(new { message = "Print job sent successfully" });
        });

        return app;
    }

    public static WebApplication UseApplicationMiddleware(this WebApplication app)
    {
        app.UseCors();
        app.UseSwagger();
        app.UseSwaggerUI();

        return app;
    }
}

