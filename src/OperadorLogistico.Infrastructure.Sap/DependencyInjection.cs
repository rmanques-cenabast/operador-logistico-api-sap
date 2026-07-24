using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OperadorLogistico.Domain.Interfaces;
using OperadorLogistico.Application.Interfaces;
using OperadorLogistico.Infrastructure.Sap.Configuration;
using OperadorLogistico.Infrastructure.Sap.Services;
using SAP.Middleware.Connector;

namespace OperadorLogistico.Infrastructure.Sap;

public static class DependencyInjection
{
    public static IServiceCollection AddSapInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Cargar las opciones de SAP desde appsettings.json
        var sapOptions = new SapOptions();
        configuration.GetSection(SapOptions.SectionName).Bind(sapOptions);
        services.Configure<SapOptions>(configuration.GetSection(SapOptions.SectionName));

        // Registrar la configuración de destino en el gestor de SAP NCo
        var destinationConfig = new SapDestinationConfiguration(sapOptions);
        try
        {
            RfcDestinationManager.RegisterDestinationConfiguration(destinationConfig);
        }
        catch (RfcInvalidStateException)
        {
            // Ya estaba registrada la configuración previamente
        }

        // Registrar los servicios aislados en el contenedor de IoC
        services.AddSingleton<ISapConnectionFactory, SapConnectionFactory>();
        services.AddScoped<ISapStatusService, SapStatusService>();
        
        // Registro de los servicios multiservicio independientes
        services.AddScoped<ISapRecepcionService, SapRecepcionService>();
        services.AddScoped<ISapCalidadService, SapCalidadService>();
        services.AddScoped<ISapTraspasoService, SapTraspasoService>();
        services.AddScoped<ISapAjusteBajaService, SapAjusteBajaService>();

        return services;
    }
}
