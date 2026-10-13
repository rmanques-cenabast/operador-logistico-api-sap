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
                        poDetail.SetValue("PURCHASEORDER", item.NumeroPedidoCompra.PadLeft(10, '0'));
                        poDetail.SetValue("ITEMS", "X"); // Activa la lectura de ítems
                        
                        poDetail.Invoke(destination);
                        
                        IRfcTable poItems = poDetail.GetTable("PO_ITEMS");
                        string itemPosFormatted = item.Posicion.ToString().PadLeft(5, '0');
                        
                        for (int j = 0; j < poItems.RowCount; j++)
                        {
                            var poItemRow = poItems[j];
                            if (poItemRow.GetString("PO_ITEM") == itemPosFormatted)
                            {
                                // 1. Extraer datos oficiales de SAP del Pedido (Unidad y Centro)
                                string sapUnit = poItemRow.GetString("UNIT");
                                string sapPlant = poItemRow.GetString("PLANT");
                                
                                // Sobrescribimos el Centro del OL con el Centro real de la OC en SAP
                                if (!string.IsNullOrEmpty(sapPlant))
                                {
                                    item.Centro = sapPlant;
                                }

                                // 2. Obtener factores de conversión utilizando las columnas descubiertas en el escáner (CONV_NUM1 y CONV_DEN1)
                                decimal convNum = 1;
                                decimal convDen = 1;

                                try { convNum = poItemRow.GetDecimal("CONV_NUM1"); } catch { }
                                try { convDen = poItemRow.GetDecimal("CONV_DEN1"); } catch { }

                                if (!string.IsNullOrEmpty(sapUnit))
                                {
                                    _logger.LogInformation("BAPI_PO_GETDETAIL - Resolución de Unidad: Pedido {Pedido} Pos {Pos} en SAP usa {Unidad}. Factores: Num={Num}, Den={Den}", 
                                        item.NumeroPedidoCompra, item.Posicion, sapUnit, convNum, convDen);

                                    // Si el OL NO envió unidad, asumimos que la cantidad ya viene en la unidad del Pedido,
                                    // por ende solo adoptamos la unidad sin hacer conversiones matemáticas.
                                    if (string.IsNullOrEmpty(item.UnidadMedida))
                                    {
                                        item.UnidadMedida = sapUnit;
                                        _logger.LogInformation("OL no envió unidad. Asumiendo unidad de la OC: {Unidad}", sapUnit);
                                    }
                                    // Si el OL sí envió una unidad distinta a la de la OC, y hay factores de conversión
                                    else if (sapUnit != item.UnidadMedida && convNum > 0 && convDen > 0)
                                    {
                                        // Aplicar conversión y redondear a 3 decimales máximo (límite estricto de BCD en SAP)
                                        decimal cantidadConvertida = Math.Round(item.Cantidad * (convDen / convNum), 3);
                                        
                                        _logger.LogInformation("Aplicando conversión: {CantOriginal} {UnOriginal} -> {CantConvertida} {UnConvertida}", 
                                            item.Cantidad, item.UnidadMedida, cantidadConvertida, sapUnit);
                                        
                                        item.Cantidad = cantidadConvertida;
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
                            item.NumeroPedidoCompra);
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
                if (!string.IsNullOrWhiteSpace(request.NumeroReferencia))
                {
                    header.SetValue("REF_DOC_NO", request.NumeroReferencia.Trim());
                }

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
                    string materialLimpio = item.Material.Trim();

                    // Formato SAP: si es puramente numérico, rellenar a 18 caracteres con ceros. Si es alfanumérico, pasarlo intacto.
                    string materialSaneado = materialLimpio.All(char.IsDigit) ? materialLimpio.PadLeft(18, '0') : materialLimpio;
                    row.SetValue("MATERIAL", materialSaneado);
                    row.SetValue("PLANT", item.Centro);
                    row.SetValue("STGE_LOC", item.Almacen);
                    row.SetValue("MOVE_TYPE", item.ClaseMovimiento);
                    row.SetValue("ENTRY_QNT", item.Cantidad.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    row.SetValue("ENTRY_UOM", item.UnidadMedida);
                    
                    // Indicador y datos del Pedido de Compra
                    row.SetValue("PO_NUMBER", item.NumeroPedidoCompra.PadLeft(10, '0'));
                    // Rellenar la posición del pedido rellenando con ceros a la izquierda (ej: "00010" para posición 10)
                    row.SetValue("PO_ITEM", item.Posicion.ToString().PadLeft(5, '0'));
                    row.SetValue("MVT_IND", "B"); // B = Pedido de compra
                    
                    if (!string.IsNullOrEmpty(item.Lote))
                    {
                        string loteSap = item.Lote.Trim();
                        if (loteSap.Length > 10)
                        {
                            loteSap = loteSap.Substring(loteSap.Length - 10);
                        }
                        row.SetValue("BATCH", loteSap);
                    }

                    if (item.FechaVencimiento.HasValue)
                    {
                        row.SetValue("EXPIRYDATE", item.FechaVencimiento.Value.ToString("yyyyMMdd"));
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

    public async Task<PedidoCompraLiberacionDto> ConsultarEstadoLiberacionAsync(string numeroPedidoCompra)
    {
        var response = new PedidoCompraLiberacionDto
        {
            NumeroPedidoCompra = numeroPedidoCompra
        };

        try
        {
            var destination = _connectionFactory.GetDestination();
            var repository = destination.Repository;
            string poFormatted = numeroPedidoCompra.Trim().PadLeft(10, '0');

            // 1. Obtener datos exactos de liberación directamente de la tabla EKKO
            try
            {
                IRfcFunction rfcReadTable = repository.CreateFunction("RFC_READ_TABLE");
                rfcReadTable.SetValue("QUERY_TABLE", "EKKO");
                rfcReadTable.SetValue("DELIMITER", "|");

                IRfcTable fieldsTable = rfcReadTable.GetTable("FIELDS");
                fieldsTable.Append(); fieldsTable.SetValue("FIELDNAME", "EBELN");
                fieldsTable.Append(); fieldsTable.SetValue("FIELDNAME", "BSART");
                fieldsTable.Append(); fieldsTable.SetValue("FIELDNAME", "LIFNR");
                fieldsTable.Append(); fieldsTable.SetValue("FIELDNAME", "AEDAT");
                fieldsTable.Append(); fieldsTable.SetValue("FIELDNAME", "FRGGR");
                fieldsTable.Append(); fieldsTable.SetValue("FIELDNAME", "FRGSX");
                fieldsTable.Append(); fieldsTable.SetValue("FIELDNAME", "FRGKE");
                fieldsTable.Append(); fieldsTable.SetValue("FIELDNAME", "FRGZU");

                IRfcTable optionsTable = rfcReadTable.GetTable("OPTIONS");
                optionsTable.Append();
                optionsTable.SetValue("TEXT", $"EBELN = '{poFormatted}'");

                rfcReadTable.Invoke(destination);

                IRfcTable dataTable = rfcReadTable.GetTable("DATA");
                if (dataTable != null && dataTable.RowCount > 0)
                {
                    string rawLine = dataTable[0].GetString("WA");
                    string[] parts = rawLine.Split('|');
                    
                    if (parts.Length >= 8)
                    {
                        response.ClaseDocumento = parts[1].Trim();
                        response.RutProveedor = parts[2].Trim();
                        string aedat = parts[3].Trim();
                        if (aedat.Length == 8)
                        {
                            response.FechaCreacion = $"{aedat.Substring(6, 2)}/{aedat.Substring(4, 2)}/{aedat.Substring(0, 4)}";
                        }
                        response.GrupoLiberacion = parts[4].Trim();
                        response.EstrategiaLiberacion = parts[5].Trim();
                        response.CodigoLiberacion = parts[6].Trim();
                        string frgzu = parts[7].Trim();

                        _logger.LogInformation("SAP EKKO para {Pedido}: Grupo={Grupo}, Estrategia={Strat}, Ind={Ind}, EstadoFirmas={Frgzu}",
                            numeroPedidoCompra, response.GrupoLiberacion, response.EstrategiaLiberacion, response.CodigoLiberacion, frgzu);

                        // Si tiene estrategia de liberación
                        if (!string.IsNullOrEmpty(response.EstrategiaLiberacion) || !string.IsNullOrEmpty(response.GrupoLiberacion))
                        {
                            // En SAP: 'X' o 'B' es Bloqueado / Pendiente. 'L' o '2' o espacio sin bloquear es Liberado
                            if (response.CodigoLiberacion == "X" || response.CodigoLiberacion == "B" || response.CodigoLiberacion != "L")
                            {
                                response.EstaLiberado = false;
                                response.EstadoLiberacionTexto = response.CodigoLiberacion == "X" ? "BLOQUEADO (Pendiente de firmas)" : "PENDIENTE DE LIBERACIÓN";
                            }
                            else
                            {
                                response.EstaLiberado = true;
                                response.EstadoLiberacionTexto = "LIBERADO TOTALMENTE";
                            }
                        }
                        else
                        {
                            response.EstaLiberado = true;
                            response.EstadoLiberacionTexto = "LIBERADO (Sin estrategia requerida)";
                        }

                        // Mapear los 4 niveles para la estrategia (PP, LP, CP, OI)
                        var codigosEstrategia = new List<(string Codigo, string Descripcion)>
                        {
                            ("PP", "Plataforma Pedido"),
                            ("LP", "Logística"),
                            ("CP", "Compras"),
                            ("OI", "OIRS")
                        };

                        for (int i = 0; i < codigosEstrategia.Count; i++)
                        {
                            var item = codigosEstrategia[i];
                            // Si en frgzu el bit correspondiente está marcado (ej. 'X' o '1' o longitud)
                            bool aprobado = !string.IsNullOrEmpty(frgzu) && frgzu.Length > i && (frgzu[i] == 'X' || frgzu[i] == '1');
                            
                            response.NivelesLiberacion.Add(new NivelLiberacionDto
                            {
                                Codigo = item.Codigo,
                                Descripcion = item.Descripcion,
                                Aprobado = response.EstaLiberado ? true : aprobado
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("RFC_READ_TABLE EKKO advertencia para {Pedido}: {Msg}", numeroPedidoCompra, ex.Message);
            }

            // Si los niveles aún están vacíos, agregar los 4 niveles por defecto
            if (response.NivelesLiberacion.Count == 0)
            {
                response.NivelesLiberacion.Add(new NivelLiberacionDto { Codigo = "PP", Descripcion = "Plataforma Pedido", Aprobado = response.EstaLiberado });
                response.NivelesLiberacion.Add(new NivelLiberacionDto { Codigo = "LP", Descripcion = "Logística", Aprobado = response.EstaLiberado });
                response.NivelesLiberacion.Add(new NivelLiberacionDto { Codigo = "CP", Descripcion = "Compras", Aprobado = response.EstaLiberado });
                response.NivelesLiberacion.Add(new NivelLiberacionDto { Codigo = "OI", Descripcion = "OIRS", Aprobado = response.EstaLiberado });
            }

            response.Exitoso = true;
            response.Mensaje = response.EstaLiberado 
                ? "El Pedido de Compra se encuentra liberado y autorizado para recepción."
                : "El Pedido de Compra requiere liberación previa en SAP por el área correspondiente.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error consultando liberación de Pedido de Compra {Pedido}", numeroPedidoCompra);
            response.Exitoso = false;
            response.Mensaje = $"Error al consultar estado en SAP: {ex.Message}";
        }

        return response;
    }
}
