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

    public Task<List<string>> QueryTableAsync(string tableName, string fields, string where = "", int rowCount = 20)
    {
        var result = new List<string>();
        try
        {
            var destination = _connectionFactory.GetDestination();
            var repository = destination.Repository;
            IRfcFunction rfc = repository.CreateFunction("RFC_READ_TABLE");
            rfc.SetValue("QUERY_TABLE", tableName.ToUpper());
            rfc.SetValue("DELIMITER", "|");
            rfc.SetValue("ROWCOUNT", rowCount);

            if (!string.IsNullOrWhiteSpace(where))
            {
                IRfcTable opt = rfc.GetTable("OPTIONS");
                var lines = SplitWhereClause(where, 65);
                foreach (var line in lines)
                {
                    IRfcStructure row = opt.Metadata.LineType.CreateStructure();
                    row.SetValue("TEXT", line);
                    opt.Append(row);
                }
            }

            if (!string.IsNullOrWhiteSpace(fields))
            {
                IRfcTable flds = rfc.GetTable("FIELDS");
                foreach (var f in fields.Split(','))
                {
                    IRfcStructure fRow = flds.Metadata.LineType.CreateStructure();
                    fRow.SetValue("FIELDNAME", f.Trim().ToUpper());
                    flds.Append(fRow);
                }
            }

            rfc.Invoke(destination);
            IRfcTable data = rfc.GetTable("DATA");
            for (int i = 0; i < data.RowCount; i++)
            {
                result.Add(data[i].GetString("WA"));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en QueryTableAsync({Table})", tableName);
            result.Add($"ERROR: {ex.Message}");
        }
        return Task.FromResult(result);
    }

    private static List<string> SplitWhereClause(string where, int maxLen = 65)
    {
        var result = new List<string>();
        var rawLines = where.Replace("\r", "").Split('\n');
        foreach (var raw in rawLines)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var words = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string current = "";
            foreach (var w in words)
            {
                if ((current + " " + w).Trim().Length <= maxLen)
                {
                    current = string.IsNullOrEmpty(current) ? w : $"{current} {w}";
                }
                else
                {
                    if (!string.IsNullOrEmpty(current))
                    {
                        result.Add(current);
                    }
                    current = w;
                }
            }
            if (!string.IsNullOrEmpty(current))
            {
                result.Add(current);
            }
        }
        return result;
    }
}
