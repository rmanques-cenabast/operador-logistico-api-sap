using Microsoft.Extensions.Logging;
using OperadorLogistico.Application.DTOs.Recepcion;
using OperadorLogistico.Application.DTOs.Shared;
using OperadorLogistico.Application.Interfaces;
using SAP.Middleware.Connector;
using System.Text;

namespace OperadorLogistico.Infrastructure.Sap.Services;

public class SapRecepcionService : ISapRecepcionService
{
    private readonly ISapConnectionFactory _connectionFactory;
    private readonly ILogger<SapRecepcionService> _logger;

    public SapRecepcionService(ISapConnectionFactory connectionFactory, ILogger<SapRecepcionService> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<SapMovimientoResponseDto> ProcesarRecepcionAsync(RecepcionPedidoRequestDto request)
    {
        var response = new SapMovimientoResponseDto { EsSimulacion = request.EsSimulacion };

        try
        {
            var destination = _connectionFactory.GetDestination();
            
            // Iniciamos un contexto transaccional explícito (RfcSessionManager) para asegurar que el COMMIT
            // se asiente en la misma sesión/conexión física de red de SAP.
            RfcSessionManager.BeginContext(destination);

            try
            {
                var repository = destination.Repository;

                // --- 🔍 AUTO-RESOLUCIÓN Y CONVERSIÓN DE UNIDAD DE MEDIDA DESDE LA OC EN SAP ---
                // Usamos BAPI_PO_GETDETAIL y la tabla PO_ITEMS con los campos exactos de factores
                foreach (var item in request.Items)
                {
                    try
                    {
                        IRfcFunction poDetail = repository.CreateFunction("BAPI_PO_GETDETAIL");
                        poDetail.SetValue("PURCHASEORDER", request.NumeroPedidoCompra.PadLeft(10, '0'));
                        poDetail.SetValue("ITEMS", "X"); // Activa la lectura de ítems
                        
                        poDetail.Invoke(destination);
                        
                        IRfcTable poItems = poDetail.GetTable("PO_ITEMS");
                        string itemPosFormatted = item.Posicion.ToString().PadLeft(5, '0');
                        
                        for (int j = 0; j < poItems.RowCount; j++)
                        {
                            var poItemRow = poItems[j];
                            if (poItemRow.GetString("PO_ITEM") == itemPosFormatted)
                            {
                                // 1. Extraer la unidad de medida oficial de SAP del Pedido (ej: 'KI')
                                string sapUnit = poItemRow.GetString("UNIT");
                                
                                // 2. Obtener factores de conversión utilizando las columnas descubiertas en el escáner (CONV_NUM1 y CONV_DEN1)
                                decimal convNum = 1;
                                decimal convDen = 1;

                                try { convNum = poItemRow.GetDecimal("CONV_NUM1"); } catch { }
                                try { convDen = poItemRow.GetDecimal("CONV_DEN1"); } catch { }

                                if (!string.IsNullOrEmpty(sapUnit))
                                {
                                    _logger.LogInformation("BAPI_PO_GETDETAIL - Resolución de Unidad: Pedido {Pedido} Pos {Pos} en SAP usa {Unidad}. Factores: Num={Num}, Den={Den}", 
                                        request.NumeroPedidoCompra, item.Posicion, sapUnit, convNum, convDen);

                                    // Si la unidad de la OC es distinta a la del OL (ej: el OL manda UN y la OC está en KI)
                                    // y los factores de conversión son válidos (> 0)
                                    if (sapUnit != item.UnidadMedida && convNum > 0 && convDen > 0)
                                    {
                                        // Aplicar conversión: Cantidad en unidad de pedido = Cantidad OL * (Denominador / Numerador)
                                        decimal cantidadConvertida = item.Cantidad * (convDen / convNum);
                                        
                                        _logger.LogInformation("Aplicando conversión: {CantOriginal} {UnOriginal} -> {CantConvertida} {UnConvertida}", 
                                            item.Cantidad, item.UnidadMedida, cantidadConvertida, sapUnit);
                                        
                                        item.Cantidad = cantidadConvertida;
                                        item.UnidadMedida = sapUnit;
                                    }
                                    else
                                    {
                                        item.UnidadMedida = sapUnit;
                                    }
                                }
                                break;
                            }
                        }
                    }
                    catch (Exception poEx)
                    {
                        _logger.LogWarning(poEx, "No se pudo auto-resolver o convertir la unidad de la OC {OC} con BAPI_PO_GETDETAIL. Se continuará con los valores originales.",
                            request.NumeroPedidoCompra);
                    }
                }

                // Instanciar la BAPI de movimiento
                IRfcFunction bapiCreate = repository.CreateFunction("BAPI_GOODSMVT_CREATE");

                // 1. Cabecera (GOODSMVT_HEADER) con fecha automática actual o la inyectada por el payload
                IRfcStructure header = bapiCreate.GetStructure("GOODSMVT_HEADER");
                var fechaDocumento = request.FechaDocumento ?? DateTime.Today;
                var fechaContabilizacion = request.FechaContabilizacion ?? DateTime.Today;
                
                header.SetValue("PSTNG_DATE", fechaContabilizacion.ToString("yyyyMMdd"));
                header.SetValue("DOC_DATE", fechaDocumento.ToString("yyyyMMdd"));
                header.SetValue("HEADER_TXT", request.TextoCabecera);

                // 2. Código de Transacción MIGO (GOODSMVT_CODE)
                IRfcStructure code = bapiCreate.GetStructure("GOODSMVT_CODE");
                code.SetValue("GM_CODE", "01"); // 01 representa entrada por pedido

                // 3. Flag de Testrun (Simulación)
                if (request.EsSimulacion)
                {
                    bapiCreate.SetValue("TESTRUN", "X");
                }

                // 4. Posiciones (GOODSMVT_ITEM)
                IRfcTable itemsTable = bapiCreate.GetTable("GOODSMVT_ITEM");

                foreach (var item in request.Items)
                {
                    IRfcStructure row = itemsTable.Metadata.LineType.CreateStructure();
                    
                    row.SetValue("MATERIAL", item.Material.PadLeft(18, '0')); // Formato numérico de material SAP de 18 caracteres
                    row.SetValue("PLANT", item.Centro);
                    row.SetValue("STGE_LOC", item.Almacen);
                    row.SetValue("MOVE_TYPE", item.ClaseMovimiento);
                    row.SetValue("ENTRY_QNT", item.Cantidad);
                    row.SetValue("ENTRY_UOM", item.UnidadMedida);
                    
                    // Indicador y datos del Pedido de Compra
                    row.SetValue("PO_NUMBER", request.NumeroPedidoCompra.PadLeft(10, '0'));
                    // Rellenar la posición del pedido rellenando con ceros a la izquierda (ej: "00010" para posición 10)
                    row.SetValue("PO_ITEM", item.Posicion.ToString().PadLeft(5, '0'));
                    row.SetValue("MVT_IND", "B"); // B = Pedido de compra
                    
                    if (!string.IsNullOrEmpty(item.Lote))
                    {
                        row.SetValue("BATCH", item.Lote);
                    }

                    if (!string.IsNullOrEmpty(item.TextoPosicion))
                    {
                        row.SetValue("ITEM_TEXT", item.TextoPosicion);
                    }

                    itemsTable.Append(row);
                }

                // 5. Invocación a SAP
                bapiCreate.Invoke(destination);

                // 6. Procesar resultados de la BAPI
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

                    if (type == "E" || type == "A")
                    {
                        tieneErrores = true;
                    }
                }

                if (!tieneErrores)
                {
                    if (!request.EsSimulacion)
                    {
                        // Confirmar la transacción para guardar en la BD de SAP
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
                        // Deshacer cambios en caso de fallos
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
            _logger.LogError(ex, "Error crítico en Recepción SAP");
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
