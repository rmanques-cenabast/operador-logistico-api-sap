using Microsoft.Extensions.Logging;
using OperadorLogistico.Application.DTOs.Calidad;
using OperadorLogistico.Application.DTOs.Shared;
using OperadorLogistico.Application.Interfaces;
using SAP.Middleware.Connector;

namespace OperadorLogistico.Infrastructure.Sap.Services;

public class SapCalidadService : ISapCalidadService
{
    private readonly ISapConnectionFactory _connectionFactory;
    private readonly ILogger<SapCalidadService> _logger;

    public SapCalidadService(ISapConnectionFactory connectionFactory, ILogger<SapCalidadService> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<SapMovimientoResponseDto> ProcesarTraspasoCalidadAsync(TraspasoCalidadRequestDto request)
    {
        var response = new SapMovimientoResponseDto { EsSimulacion = request.EsSimulacion };
        try
        {
            var destination = _connectionFactory.GetDestination();
            var repository = destination.Repository;
            IRfcFunction bapiCreate = repository.CreateFunction("BAPI_GOODSMVT_CREATE");

            bapiCreate.GetStructure("GOODSMVT_HEADER").SetValue("PSTNG_DATE", request.FechaContabilizacion.ToString("yyyyMMdd"));
            bapiCreate.GetStructure("GOODSMVT_HEADER").SetValue("DOC_DATE", request.FechaDocumento.ToString("yyyyMMdd"));
            bapiCreate.GetStructure("GOODSMVT_HEADER").SetValue("HEADER_TXT", request.TextoCabecera);

            bapiCreate.GetStructure("GOODSMVT_CODE").SetValue("GM_CODE", "04"); // 04 = Traspaso/Transferencia

            if (request.EsSimulacion) bapiCreate.SetValue("TESTRUN", "X");

            IRfcTable itemsTable = bapiCreate.GetTable("GOODSMVT_ITEM");
            foreach (var item in request.Items)
            {
                IRfcStructure row = itemsTable.Metadata.LineType.CreateStructure();
                row.SetValue("MATERIAL", item.Material.PadLeft(18, '0'));
                row.SetValue("PLANT", item.Centro);
                row.SetValue("STGE_LOC", item.AlmacenOrigen);
                row.SetValue("MOVE_STLOC", item.AlmacenDestino); // Almacén destino en traspasos
                row.SetValue("MOVE_TYPE", item.ClaseMovimiento);
                row.SetValue("ENTRY_QNT", item.Cantidad);
                row.SetValue("ENTRY_UOM", item.UnidadMedida);

                if (!string.IsNullOrEmpty(item.Lote)) row.SetValue("BATCH", item.Lote);
                itemsTable.Append(row);
            }

            await InvocacionBaseBapi(bapiCreate, destination, response, request.EsSimulacion);
        }
        catch (Exception ex)
        {
            ManejarExcepcion(ex, response);
        }
        return response;
    }

    public async Task<SapMovimientoResponseDto> ProcesarMuestreoCalidadAsync(MuestreoCalidadRequestDto request)
    {
        var response = new SapMovimientoResponseDto { EsSimulacion = request.EsSimulacion };
        try
        {
            var destination = _connectionFactory.GetDestination();
            var repository = destination.Repository;
            IRfcFunction bapiCreate = repository.CreateFunction("BAPI_GOODSMVT_CREATE");

            bapiCreate.GetStructure("GOODSMVT_HEADER").SetValue("PSTNG_DATE", request.FechaContabilizacion.ToString("yyyyMMdd"));
            bapiCreate.GetStructure("GOODSMVT_HEADER").SetValue("DOC_DATE", request.FechaDocumento.ToString("yyyyMMdd"));
            bapiCreate.GetStructure("GOODSMVT_HEADER").SetValue("HEADER_TXT", request.TextoCabecera);

            bapiCreate.GetStructure("GOODSMVT_CODE").SetValue("GM_CODE", "03"); // 03 = Salida de mercancías

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

            await InvocacionBaseBapi(bapiCreate, destination, response, request.EsSimulacion);
        }
        catch (Exception ex)
        {
            ManejarExcepcion(ex, response);
        }
        return response;
    }

    private Task InvocacionBaseBapi(IRfcFunction bapiCreate, RfcDestination destination, SapMovimientoResponseDto response, bool esSimulacion)
    {
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
                CodigoMensaje = row.GetString("MESSAGE_V1")
            });

            if (type == "E" || type == "A") tieneErrores = true;
        }

        if (!tieneErrores)
        {
            if (!esSimulacion)
            {
                IRfcFunction bapiCommit = destination.Repository.CreateFunction("BAPI_TRANSACTION_COMMIT");
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
            if (!esSimulacion)
            {
                IRfcFunction bapiRollback = destination.Repository.CreateFunction("BAPI_TRANSACTION_ROLLBACK");
                bapiRollback.Invoke(destination);
            }
        }
        return Task.CompletedTask;
    }

    private void ManejarExcepcion(Exception ex, SapMovimientoResponseDto response)
    {
        _logger.LogError(ex, "Error crítico en proceso de calidad en SAP");
        response.Exitoso = false;
        response.Mensajes.Add(new BapiReturnMessageDto
        {
            Tipo = "A",
            Mensaje = $"Error de comunicación/ejecución SAP: {ex.Message}"
        });
    }
}
