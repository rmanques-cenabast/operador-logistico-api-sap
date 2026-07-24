using Microsoft.Extensions.Logging;
using OperadorLogistico.Domain.Interfaces;
using SAP.Middleware.Connector;

namespace OperadorLogistico.Infrastructure.Sap.Services;

public class SapStatusService : ISapStatusService
{
    private readonly ISapConnectionFactory _connectionFactory;
    private readonly ILogger<SapStatusService> _logger;

    public SapStatusService(ISapConnectionFactory connectionFactory, ILogger<SapStatusService> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public Task<string> GetSapStatusAsync()
    {
        try
        {
            var destination = _connectionFactory.GetDestination();
            destination.Ping();
            return Task.FromResult($"Conexión EXITOSA con SAP [{destination.SystemID} - Client {destination.Client}]");
        }
        catch (RfcBaseException ex)
        {
            _logger.LogError(ex, "Error al conectar con el servidor SAP NCo");
            return Task.FromResult($"Error RFC SAP: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error insospechado al intentar Ping con SAP");
            return Task.FromResult($"Error General: {ex.Message}");
        }
    }

    public Task<object> VerificarBapiAsync(string bapiName)
    {
        try
        {
            var destination = _connectionFactory.GetDestination();
            var repository = destination.Repository;
            
            IRfcFunction function = repository.CreateFunction(bapiName.ToUpper());

            if (function == null)
            {
                return Task.FromResult<object>(new
                {
                    Habilitada = false,
                    Mensaje = $"La BAPI '{bapiName}' no fue encontrada en SAP."
                });
            }

            var parametros = new List<object>();

            for (int i = 0; i < function.Metadata.ParameterCount; i++)
            {
                var param = function.Metadata[i];
                parametros.Add(new
                {
                    Nombre = param.Name,
                    Direccion = param.Direction.ToString(),
                    TipoDatos = param.DataType.ToString(),
                    Documentacion = param.Documentation
                });
            }

            var respuestaFormateada = new
            {
                NombreOficial = function.Metadata.Name,
                HabilitadaRfc = true,
                TotalParametros = function.Metadata.ParameterCount,
                Parametros = parametros
            };

            return Task.FromResult<object>(respuestaFormateada);
        }
        catch (RfcBaseException ex)
        {
            _logger.LogError(ex, "Error de SAP RFC al consultar la BAPI {BapiName}", bapiName);
            return Task.FromResult<object>(new
            {
                HabilitadaRfc = false,
                Error = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al verificar la BAPI {BapiName}", bapiName);
            return Task.FromResult<object>(new
            {
                HabilitadaRfc = false,
                Error = ex.Message
            });
        }
    }
}
