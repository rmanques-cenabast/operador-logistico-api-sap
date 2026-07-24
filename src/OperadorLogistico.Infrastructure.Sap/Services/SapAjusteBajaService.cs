using Microsoft.Extensions.Logging;
using OperadorLogistico.Application.DTOs.AjustesBajas;
using OperadorLogistico.Application.DTOs.Shared;
using OperadorLogistico.Application.Interfaces;
using SAP.Middleware.Connector;

namespace OperadorLogistico.Infrastructure.Sap.Services;

public class SapAjusteBajaService : ISapAjusteBajaService
{
    private readonly ISapConnectionFactory _connectionFactory;
    private readonly ILogger<SapAjusteBajaService> _logger;

    public SapAjusteBajaService(ISapConnectionFactory connectionFactory, ILogger<SapAjusteBajaService> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<SapMovimientoResponseDto> ProcesarAjusteBajaAsync(AjusteBajaRequestDto request)
    {
        var response = new SapMovimientoResponseDto { EsSimulacion = request.EsSimulacion };
        try
        {
            var destination = _connectionFactory.GetDestination();
            
            // Iniciar contexto de sesión de red transaccional para asegurar el COMMIT
            RfcSessionManager.BeginContext(destination);

            try
            {
                var repository = destination.Repository;
                IRfcFunction bapiCreate = repository.CreateFunction("BAPI_GOODSMVT_CREATE");

                // Asignar fechas: la enviada por Express, de lo contrario la de hoy
                var fechaDocumento = request.FechaDocumento ?? DateTime.Today;
                var fechaContabilizacion = request.FechaContabilizacion ?? DateTime.Today;

                bapiCreate.GetStructure("GOODSMVT_HEADER").SetValue("PSTNG_DATE", fechaContabilizacion.ToString("yyyyMMdd"));
                bapiCreate.GetStructure("GOODSMVT_HEADER").SetValue("DOC_DATE", fechaDocumento.ToString("yyyyMMdd"));
                bapiCreate.GetStructure("GOODSMVT_HEADER").SetValue("HEADER_TXT", request.TextoCabecera);

                // Determinar GM_CODE: 
                // 555 (Desguace) requiere GM_CODE "03" (Salidas)
                // 711, 712, 717, 718 (Diferencias de inventario físico) requieren GM_CODE "05" (Otras entradas)
                bool esDesguace = request.Items.Any(i => i.ClaseMovimiento == "555" || i.ClaseMovimiento == "556");
                string gmCode = esDesguace ? "03" : "05";
                bapiCreate.GetStructure("GOODSMVT_CODE").SetValue("GM_CODE", gmCode);

                if (request.EsSimulacion) bapiCreate.SetValue("TESTRUN", "X");

                IRfcTable itemsTable = bapiCreate.GetTable("GOODSMVT_ITEM");
                foreach (var item in request.Items)
                {
                    IRfcStructure row = itemsTable.Metadata.LineType.CreateStructure();
                    row.SetValue("MATERIAL", item.Material.PadLeft(18, '0'));
                    row.SetValue("PLANT", item.Centro);
                    row.SetValue("STGE_LOC", item.Almacen);
                    row.SetValue("MOVE_TYPE", item.ClaseMovimiento);
                    row.SetValue("ENTRY_QNT", item.Cantidad);
                    row.SetValue("ENTRY_UOM", item.UnidadMedida);

                    if (!string.IsNullOrEmpty(item.CentroCosto))
                    {
                        row.SetValue("COSTCENTER", item.CentroCosto.PadLeft(10, '0'));
                    }

                    if (!string.IsNullOrEmpty(item.Lote)) row.SetValue("BATCH", item.Lote);
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
                        CodigoMensaje = row.GetString("MESSAGE_V1"),
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
                RfcSessionManager.EndContext(destination);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error crítico en proceso de ajustes/bajas en SAP");
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
