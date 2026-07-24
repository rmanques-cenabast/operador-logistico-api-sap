using Microsoft.Extensions.Logging;
using OperadorLogistico.Application.DTOs.Shared;
using OperadorLogistico.Application.DTOs.Traspasos;
using OperadorLogistico.Application.Interfaces;
using SAP.Middleware.Connector;

namespace OperadorLogistico.Infrastructure.Sap.Services;

public class SapTraspasoService : ISapTraspasoService
{
    private readonly ISapConnectionFactory _connectionFactory;
    private readonly ILogger<SapTraspasoService> _logger;

    public SapTraspasoService(ISapConnectionFactory connectionFactory, ILogger<SapTraspasoService> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<SapMovimientoResponseDto> ProcesarTraspasoAsync(TraspasoRequestDto request)
    {
        var response = new SapMovimientoResponseDto { EsSimulacion = request.EsSimulacion };
        try
        {
            var destination = _connectionFactory.GetDestination();
            
            // Iniciamos un contexto transaccional explícito (RfcSessionManager) para asegurar que el COMMIT
            // se ejecute sobre la misma sesión y canal de comunicación exacto donde se llamó la BAPI.
            RfcSessionManager.BeginContext(destination);
            
            try
            {
                var repository = destination.Repository;
                IRfcFunction bapiCreate = repository.CreateFunction("BAPI_GOODSMVT_CREATE");

                // Fechas calculadas internamente de forma automática
                var fechaHoy = DateTime.Today.ToString("yyyyMMdd");

                bapiCreate.GetStructure("GOODSMVT_HEADER").SetValue("PSTNG_DATE", fechaHoy);
                bapiCreate.GetStructure("GOODSMVT_HEADER").SetValue("DOC_DATE", fechaHoy);
                bapiCreate.GetStructure("GOODSMVT_HEADER").SetValue("HEADER_TXT", request.TextoCabecera);

                bapiCreate.GetStructure("GOODSMVT_CODE").SetValue("GM_CODE", "04"); // 04 = Traspaso/Transferencia

                if (request.EsSimulacion) bapiCreate.SetValue("TESTRUN", "X");

                IRfcTable itemsTable = bapiCreate.GetTable("GOODSMVT_ITEM");
                foreach (var item in request.Items)
                {
                    IRfcStructure row = itemsTable.Metadata.LineType.CreateStructure();
                    row.SetValue("MATERIAL", item.Material.PadLeft(18, '0'));
                    row.SetValue("PLANT", item.DesdeCentro);
                    row.SetValue("STGE_LOC", item.DesdeAlmacen);
                    row.SetValue("MOVE_TYPE", item.ClaseMovimiento);
                    row.SetValue("ENTRY_QNT", item.Cantidad);
                    row.SetValue("ENTRY_UOM", item.UnidadMedida);

                    // Imputación de Centro Destino (haciaCentro)
                    if (!string.IsNullOrEmpty(item.HaciaCentro))
                    {
                        row.SetValue("MOVE_PLANT", item.HaciaCentro);
                    }

                    // Imputación de Almacén Destino (haciaAlmacen)
                    if (!string.IsNullOrEmpty(item.HaciaAlmacen))
                    {
                        row.SetValue("MOVE_STLOC", item.HaciaAlmacen);
                    }

                    // Imputación de Material Destino si aplica (Movimiento 309)
                    if (!string.IsNullOrEmpty(item.MaterialDestino))
                    {
                        row.SetValue("MOVE_MAT", item.MaterialDestino.PadLeft(18, '0'));
                    }

                    // Lotes (desdeLote / haciaLote)
                    if (!string.IsNullOrEmpty(item.DesdeLote))
                    {
                        row.SetValue("BATCH", item.DesdeLote);
                    }
                    
                    // En BAPI_GOODSMVT_CREATE el lote de destino se mapea en MOVE_BATCH
                    if (!string.IsNullOrEmpty(item.HaciaLote))
                    {
                        row.SetValue("MOVE_BATCH", item.HaciaLote); 
                    }

                    // Asignación del Estado/Tipo de Stock Destino (STGE_TYPE)
                    // Útil para traspasos cruzados (ej: mover de disponible a bloqueado en un movimiento 311)
                    if (!string.IsNullOrEmpty(item.TipoStockDestino))
                    {
                        row.SetValue("STGE_TYPE", item.TipoStockDestino);
                    }

                    itemsTable.Append(row);
                }

                bapiCreate.Invoke(destination);

                var docMaterial = bapiCreate.GetString("MATERIALDOCUMENT");
                var ejercicio = bapiCreate.GetString("MATDOCUMENTYEAR");
                IRfcTable returnTable = bapiCreate.GetTable("RETURN");

                bool tieneErrores = false;
                for (int i = 0; i < returnTable.RowCount; i++)
                {
                    var row = returnTable[i];
                    var type = row.GetString("TYPE");
                    
                    response.Mensajes.Add(new BapiReturnMessageDto
                    {
                        Tipo = type,
                        Mensaje = row.GetString("MESSAGE"),
                        IdMensaje = row.GetString("ID"),
                        NumeroMensaje = row.GetString("NUMBER"),
                        Variable1 = row.GetString("MESSAGE_V1"),
                        Variable2 = row.GetString("MESSAGE_V2"),
                        Variable3 = row.GetString("MESSAGE_V3"),
                        Variable4 = row.GetString("MESSAGE_V4"),
                        Parametro = row.GetString("PARAMETER"),
                        Fila = row.GetInt("ROW")
                    });

                    if (type == "E" || type == "A") tieneErrores = true;
                }

                if (!tieneErrores)
                {
                    if (!request.EsSimulacion)
                    {
                        IRfcFunction bapiCommit = repository.CreateFunction("BAPI_TRANSACTION_COMMIT");
                        bapiCommit.SetValue("WAIT", "X");
                        bapiCommit.Invoke(destination);
                    }
                    response.Exitoso = true;
                    response.DocumentoMaterial = docMaterial;
                    response.Ejercicio = ejercicio;
                }
                else
                {
                    response.Exitoso = false;
                    if (!request.EsSimulacion)
                    {
                        IRfcFunction bapiRollback = repository.CreateFunction("BAPI_TRANSACTION_ROLLBACK");
                        bapiRollback.Invoke(destination);
                    }
                }
            }
            finally
            {
                // Cerramos el contexto de sesión explícito de forma segura
                RfcSessionManager.EndContext(destination);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error crítico en proceso de traspaso en SAP");
            response.Exitoso = false;
            response.Mensajes.Add(new BapiReturnMessageDto
            {
                Tipo = "A",
                Mensaje = $"Error de comunicación/ejecución SAP: {ex.Message}"
            });
        }
        return response;
    }
}
