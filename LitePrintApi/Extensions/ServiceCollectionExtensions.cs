using LitePrintApi.Services;

namespace LitePrintApi.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo { Title = "LitePrint API", Version = "v1" });
        });
        
        services.AddCors(o => o.AddDefaultPolicy(p => p
            .AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod()));

        // Registrar servicios de aplicación
        services.AddScoped<IPrinterService, PrinterService>();

        return services;
    }
}

