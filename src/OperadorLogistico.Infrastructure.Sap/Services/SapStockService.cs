using Microsoft.Extensions.Logging;
using OperadorLogistico.Application.DTOs.Inventario;
using OperadorLogistico.Application.DTOs.Shared;
using OperadorLogistico.Application.Interfaces;
using SAP.Middleware.Connector;
using System.Globalization;

namespace OperadorLogistico.Infrastructure.Sap.Services;

public class SapStockService : ISapStockService
{
    private readonly ISapConnectionFactory _connectionFactory;
    private readonly ILogger<SapStockService> _logger;

    public SapStockService(ISapConnectionFactory connectionFactory, ILogger<SapStockService> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<ConsultaStockResponseDto> ConsultarStockLoteAsync(ConsultaStockRequestDto request)
    {
        var response = new ConsultaStockResponseDto();
        try
        {
            var destination = _connectionFactory.GetDestination();
            var repository = destination.Repository;

            IRfcFunction rfcReadTable = repository.CreateFunction("RFC_READ_TABLE");
            rfcReadTable.SetValue("QUERY_TABLE", "MCHB");
            rfcReadTable.SetValue("DELIMITER", "|");

            IRfcTable options = rfcReadTable.GetTable("OPTIONS");
            
            // RFC_READ_TABLE tiene un límite de 72 caracteres por línea. Dividimos la consulta en dos.
            IRfcStructure optRow1 = options.Metadata.LineType.CreateStructure();
            optRow1.SetValue("TEXT", $"MATNR = '{request.Material.PadLeft(18, '0')}' AND WERKS = '{request.Centro}'");
            options.Append(optRow1);
            
            IRfcStructure optRow2 = options.Metadata.LineType.CreateStructure();
            optRow2.SetValue("TEXT", $" AND LGORT = '{request.Almacen}' AND CHARG = '{request.Lote}'");
            options.Append(optRow2);

            IRfcTable fields = rfcReadTable.GetTable("FIELDS");
            string[] fieldNames = { "CLABS", "CINSM", "CSPEM" };
            foreach (var f in fieldNames)
            {
                IRfcStructure fRow = fields.Metadata.LineType.CreateStructure();
                fRow.SetValue("FIELDNAME", f);
                fields.Append(fRow);
            }

            rfcReadTable.Invoke(destination);

            IRfcTable data = rfcReadTable.GetTable("DATA");
            if (data.RowCount > 0)
            {
                string line = data[0].GetString("WA");
                string[] values = line.Split('|');
                
                if (values.Length > 0 && decimal.TryParse(values[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal valLibre)) response.Libre = valLibre;
                if (values.Length > 1 && decimal.TryParse(values[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal valCalidad)) response.Calidad = valCalidad;
                if (values.Length > 2 && decimal.TryParse(values[2].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal valBloqueado)) response.Bloqueado = valBloqueado;

                response.Exitoso = true;
            }
            else
            {
                response.Exitoso = false;
                response.Mensajes.Add(new BapiReturnMessageDto { Mensaje = "No se encontró el lote en SAP." });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al consultar stock en SAP");
            response.Exitoso = false;
            response.Mensajes.Add(new BapiReturnMessageDto { Mensaje = ex.Message });
        }

        return await Task.FromResult(response);
    }
}
