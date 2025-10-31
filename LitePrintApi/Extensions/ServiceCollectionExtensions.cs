using LitePrintApi.Services;

namespace LitePrintApi.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();
        
        services.AddCors(o => o.AddDefaultPolicy(p => p
            .AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod()));

        // Registrar servicios de aplicación
        services.AddScoped<IPrinterService, PrinterService>();

        return services;
    }
}

