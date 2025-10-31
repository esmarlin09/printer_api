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

