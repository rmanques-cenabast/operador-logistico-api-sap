using Microsoft.Extensions.Logging;
using OperadorLogistico.Application.DTOs.Salida;
using OperadorLogistico.Application.DTOs.Shared;
using OperadorLogistico.Application.Interfaces;
using SAP.Middleware.Connector;
using System.Globalization;

namespace OperadorLogistico.Infrastructure.Sap.Services;

public class SapSalidaService : ISapSalidaService
{
    private readonly ISapConnectionFactory _connectionFactory;
    private readonly ILogger<SapSalidaService> _logger;

    public SapSalidaService(ISapConnectionFactory connectionFactory, ILogger<SapSalidaService> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<ConsultaEntregasResponseDto> ConsultarEntregasSalidaAsync(ConsultaEntregasRequestDto request)
    {
        var fechaDesde = request.FechaDesde ?? new DateTime(2026, 1, 1);
        var fechaHasta = request.FechaHasta ?? new DateTime(2026, 12, 31);

        var response = new ConsultaEntregasResponseDto
        {
            Exitoso = true,
            FechaDesde = fechaDesde,
            FechaHasta = fechaHasta
        };

        try
        {
            _logger.LogInformation("Consultando entregas de salida en SAP entre {Desde:yyyy-MM-dd} y {Hasta:yyyy-MM-dd} para centro {Centro}",
                fechaDesde, fechaHasta, request.Centro);

            var destination = _connectionFactory.GetDestination();
            var repository = destination.Repository;

            // Intentamos consultar mediante BAPI_OUTB_DELIVERY_GETLIST o RFC_READ_TABLE de LIKP/LIPS
            try
            {
                IRfcFunction rfcReadTable = repository.CreateFunction("RFC_READ_TABLE");
                rfcReadTable.SetValue("QUERY_TABLE", "LIKP");
                rfcReadTable.SetValue("DELIMITER", "|");

                string sDesde = fechaDesde.ToString("yyyyMMdd");
                string sHasta = fechaHasta.ToString("yyyyMMdd");

                IRfcTable options = rfcReadTable.GetTable("OPTIONS");
                IRfcStructure optRow1 = options.Metadata.LineType.CreateStructure();
                optRow1.SetValue("TEXT", $"LFDAT >= '{sDesde}' AND LFDAT <= '{sHasta}'");
                options.Append(optRow1);

                // Filtro por Canal / Organización de Ventas (FP: 5000, INT: 1000 / 3000)
                if (string.Equals(request.Canal, "FP", StringComparison.OrdinalIgnoreCase) || request.OrganizacionVenta == "5000")
                {
                    IRfcStructure optRowVkorg = options.Metadata.LineType.CreateStructure();
                    optRowVkorg.SetValue("TEXT", " AND VKORG = '5000'");
                    options.Append(optRowVkorg);
                }
                else if (string.Equals(request.Canal, "INT", StringComparison.OrdinalIgnoreCase) || request.OrganizacionVenta == "1000" || request.OrganizacionVenta == "3000")
                {
                    IRfcStructure optRowVkorg = options.Metadata.LineType.CreateStructure();
                    optRowVkorg.SetValue("TEXT", " AND ( VKORG = '1000' OR VKORG = '3000' )");
                    options.Append(optRowVkorg);
                }
                else
                {
                    IRfcStructure optRowVkorg = options.Metadata.LineType.CreateStructure();
                    optRowVkorg.SetValue("TEXT", " AND ( VKORG = '5000' OR VKORG = '1000' OR VKORG = '3000' )");
                    options.Append(optRowVkorg);
                }

                rfcReadTable.SetValue("ROWCOUNT", 100);
                rfcReadTable.SetValue("ROWSKIPS", request.Desplazamiento);

                // Campos reales y universales de LIKP en SAP ECC (WADAT_IST almacena la fecha real de contabilización de salida)
                IRfcTable fields = rfcReadTable.GetTable("FIELDS");
                string[] fieldNames = { "VBELN", "ERDAT", "ERNAM", "WADAT", "LFDAT", "KUNNR", "LFART", "VSTEL", "ABLAD", "LPRIO", "WADAT_IST", "VKORG", "KUNAG" };
                foreach (var f in fieldNames)
                {
                    IRfcStructure fRow = fields.Metadata.LineType.CreateStructure();
                    fRow.SetValue("FIELDNAME", f);
                    fields.Append(fRow);
                }

                _logger.LogInformation("Ejecutando RFC_READ_TABLE en LIKP entre {Desde} y {Hasta} (SoloPendientes: {Pend}, Canal: {Canal})", sDesde, sHasta, request.SoloPendientes, request.Canal ?? "TODOS");
                rfcReadTable.Invoke(destination);

                IRfcTable data = rfcReadTable.GetTable("DATA");
                _logger.LogInformation("LIKP devolvió {Count} entregas de salida desde SAP", data.RowCount);

                int maxToProcess = data.RowCount;
                response.LeidasSap = data.RowCount;
                response.SiguienteDesplazamiento = checked(request.Desplazamiento + data.RowCount);
                // Continue even when all rows of a full block are excluded by SM/channel.
                response.HayMas = data.RowCount == 100;

                // 1. Recolectar KUNNRs y VBELNs para consultar KNA1 y VBUK en bloque
                var uniqueKunnrs = new HashSet<string>();
                var vbelnList = new List<string>();
                for (int i = 0; i < maxToProcess; i++)
                {
                    string line = data[i].GetString("WA");
                    string[] cols = line.Split('|');
                    if (cols.Length > 0 && !string.IsNullOrWhiteSpace(cols[0]))
                    {
                        vbelnList.Add(cols[0].Trim().PadLeft(10, '0'));
                    }
                    if (cols.Length > 5 && !string.IsNullOrWhiteSpace(cols[5]))
                    {
                        uniqueKunnrs.Add(cols[5].Trim().TrimStart('0'));
                    }
                }
                var clientesDict = ObtenerDatosClientes(destination, repository, uniqueKunnrs.ToList());
                var wbstkDict = ObtenerEstadosSalidaMercancia(destination, repository, vbelnList);

                for (int i = 0; i < maxToProcess; i++)
                {
                    string line = data[i].GetString("WA");
                    string[] cols = line.Split('|');
                    if (cols.Length >= 4)
                    {
                        string rawVbeln = cols[0].Trim();
                        string vbeln = rawVbeln.TrimStart('0');
                        string erdatStr = cols[1].Trim();
                        string ernam = cols.Length > 2 ? cols[2].Trim() : "";
                        string wadatStr = cols.Length > 3 ? cols[3].Trim() : "";
                        string lfdatStr = cols.Length > 4 ? cols[4].Trim() : "";
                        string kunnr = cols.Length > 5 ? cols[5].Trim().TrimStart('0') : "";
                        string lfart = cols.Length > 6 ? cols[6].Trim() : "";
                        string vstel = cols.Length > 7 ? cols[7].Trim() : "";
                        string ablad = cols.Length > 8 ? cols[8].Trim() : "";
                        string lprio = cols.Length > 9 ? cols[9].Trim() : "";
                        string vkorg = cols.Length > 11 ? cols[11].Trim() : "";
                        string kunag = cols.Length > 12 ? cols[12].Trim().TrimStart('0') : "";

                        DateTime.TryParseExact(erdatStr, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime fCreacion);
                        DateTime.TryParseExact(lfdatStr, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime fCompromiso);
                        if (fCompromiso == default)
                        {
                            DateTime.TryParseExact(wadatStr, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out fCompromiso);
                        }
                        if (fCreacion == default) fCreacion = DateTime.Now;
                        if (fCompromiso == default) fCompromiso = fCreacion.AddDays(1);

                        string prioridad = "NORMAL";
                        if (lprio == "01" || lprio == "1") prioridad = "URGENTE";
                        else if (lprio == "02" || lprio == "2") prioridad = "ALTA";

                        // Determinar Canal: 5000 = Farmacias Privadas (FP), 1000/3000 = Intermediación (INT)
                        string canal = (vkorg == "5000") ? "FP" : "INT";

                        string rawVbelnPad = rawVbeln.PadLeft(10, '0');
                        string wbstk = wbstkDict.TryGetValue(rawVbelnPad, out var sWb) ? sWb : "A";

                        if (request.SoloPendientes && wbstk != "A")
                        {
                            continue;
                        }

                        string estado = (wbstk == "C") ? "DESPACHADO" : (wbstk == "B" ? "EN_PICKING" : "PENDIENTE_PICKING");
                        string estadoSap = (wbstk == "C") ? "CONTABILIZADO" : (wbstk == "B" ? "EN_PROCESO" : "PENDIENTE");

                        // Recuperar datos reales desde KNA1
                        var kna1 = clientesDict.TryGetValue(kunnr, out var info) ? info : (Name1: "", Name2: "", Stras: "", Ort01: "");

                        string destinatario = !string.IsNullOrWhiteSpace(kna1.Name1) 
                            ? kna1.Name1 
                            : (!string.IsNullOrWhiteSpace(ablad) ? $"ESTABLECIMIENTO ({ablad})" : $"CLIENTE KUNNR {kunnr}");

                        string direccion = !string.IsNullOrWhiteSpace(kna1.Stras) 
                            ? kna1.Stras 
                            : (!string.IsNullOrWhiteSpace(ablad) ? ablad : "RED ASISTENCIAL CENABAST");

                        string comuna = !string.IsNullOrWhiteSpace(kna1.Ort01) ? kna1.Ort01 : "SANTIAGO";

                        // Determinar Tipo de Destinatario
                        string tipoDestinatario = "ESTABLECIMIENTO SALUD";
                        if (canal == "FP")
                        {
                            tipoDestinatario = "FARMACIA PRIVADA";
                        }
                        else
                        {
                            string nUpper = $"{kna1.Name1} {kna1.Name2}".ToUpperInvariant();
                            if (nUpper.Contains("HOSPITAL") || nUpper.Contains("COMPLEJO")) tipoDestinatario = "HOSPITAL";
                            else if (nUpper.Contains("CESFAM") || nUpper.Contains("CONSULTORIO") || nUpper.Contains("POSTA") || nUpper.Contains("APS")) tipoDestinatario = "CESFAM / APS";
                            else if (nUpper.Contains("MUNICIPAL") || nUpper.Contains("ILUSTRE")) tipoDestinatario = "MUNICIPALIDAD";
                            else if (nUpper.Contains("SERVICIO DE SALUD") || nUpper.Contains("DIRECCION")) tipoDestinatario = "SERVICIO DE SALUD";
                            else tipoDestinatario = "ESTABLECIMIENTO SALUD";
                        }

                        var entregaDto = new SapEntregaSalidaDto
                        {
                            NumeroEntrega = vbeln,
                            PedidoVenta = "PED-" + vbeln,
                            DocModelo = "PED-" + vbeln,
                            CodigoDestinatario = kunnr,
                            Solicitante = kunag,
                            Destinatario = destinatario,
                            DireccionDestino = direccion,
                            Comuna = comuna,
                            Ciudad = comuna,
                            Prioridad = prioridad,
                            Canal = canal,
                            OrganizacionVenta = string.IsNullOrWhiteSpace(vkorg) ? (canal == "FP" ? "5000" : "1000") : vkorg,
                            TipoDestinatario = tipoDestinatario,
                            FechaCreacion = fCreacion,
                            FechaCompromiso = fCompromiso,
                            Estado = estado,
                            EstadoSap = estadoSap
                        };

                        // Consultar posiciones LIPS con número de entrega a 10 dígitos
                        ConsultarPosicionesEntrega(destination, repository, rawVbeln.PadLeft(10, '0'), request.Centro, entregaDto);

                        if (entregaDto.Lineas.Count == 0)
                        {
                            continue;
                        }

                        response.Entregas.Add(entregaDto);
                    }
                }
            }
            catch (Exception exRfc)
            {
                _logger.LogWarning(exRfc, "RFC_READ_TABLE LIKP error en llamada: {Msg}. No se devolveran entregas sin validar.", exRfc.Message);
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Conexión a SAP no disponible o error general: {Msg}. No se devolveran entregas sin validar.", ex.Message);
            response.Exitoso = false;
            response.Entregas.Clear();
            response.Mensajes.Add(new BapiReturnMessageDto
            {
                Tipo = "E",
                Mensaje = "No fue posible consultar las entregas y validar sus filtros en SAP."
            });
        }

        response.TotalEntregas = response.Entregas.Count;
        return await Task.FromResult(response);
    }

    private Dictionary<string, (string Name1, string Name2, string Stras, string Ort01)> ObtenerDatosClientes(
        RfcDestination destination, 
        RfcRepository repository, 
        List<string> kunnrs)
    {
        var dict = new Dictionary<string, (string Name1, string Name2, string Stras, string Ort01)>();
        var distinct = kunnrs.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct().ToList();
        if (distinct.Count == 0) return dict;

        int chunkSize = 20;
        for (int i = 0; i < distinct.Count; i += chunkSize)
        {
            var chunk = distinct.Skip(i).Take(chunkSize).Select(k => k.PadLeft(10, '0')).ToList();
            try
            {
                IRfcFunction rfcKna1 = repository.CreateFunction("RFC_READ_TABLE");
                rfcKna1.SetValue("QUERY_TABLE", "KNA1");
                rfcKna1.SetValue("DELIMITER", "|");

                IRfcTable options = rfcKna1.GetTable("OPTIONS");
                for (int ci = 0; ci < chunk.Count; ci++)
                {
                    IRfcStructure oRow = options.Metadata.LineType.CreateStructure();
                    oRow.SetValue("TEXT", (ci == 0 ? "" : "OR ") + $"KUNNR = '{chunk[ci]}'");
                    options.Append(oRow);
                }

                IRfcTable fields = rfcKna1.GetTable("FIELDS");
                string[] fNames = { "KUNNR", "NAME1", "NAME2", "STRAS", "ORT01" };
                foreach (var f in fNames)
                {
                    IRfcStructure fRow = fields.Metadata.LineType.CreateStructure();
                    fRow.SetValue("FIELDNAME", f);
                    fields.Append(fRow);
                }

                rfcKna1.Invoke(destination);
                IRfcTable data = rfcKna1.GetTable("DATA");
                for (int j = 0; j < data.RowCount; j++)
                {
                    string raw = data[j].GetString("WA");
                    string[] parts = raw.Split('|');
                    if (parts.Length >= 2)
                    {
                        string k = parts[0].Trim().TrimStart('0');
                        string n1 = parts[1].Trim();
                        string n2 = parts.Length > 2 ? parts[2].Trim() : "";
                        string stras = parts.Length > 3 ? parts[3].Trim() : "";
                        string ort01 = parts.Length > 4 ? parts[4].Trim() : "";
                        dict[k] = (n1, n2, stras, ort01);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Error consultando lote KNA1: {Msg}", ex.Message);
            }
        }
        return dict;
    }

    private Dictionary<string, string> ObtenerEstadosSalidaMercancia(
        RfcDestination destination,
        RfcRepository repository,
        List<string> vbelns)
    {
        var dict = new Dictionary<string, string>();
        var distinct = vbelns.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct().ToList();
        if (distinct.Count == 0) return dict;

        int chunkSize = 20;
        for (int i = 0; i < distinct.Count; i += chunkSize)
        {
            var chunk = distinct.Skip(i).Take(chunkSize).Select(v => v.PadLeft(10, '0')).ToList();
            try
            {
                IRfcFunction rfcVbuk = repository.CreateFunction("RFC_READ_TABLE");
                rfcVbuk.SetValue("QUERY_TABLE", "VBUK");
                rfcVbuk.SetValue("DELIMITER", "|");

                IRfcTable options = rfcVbuk.GetTable("OPTIONS");
                for (int ci = 0; ci < chunk.Count; ci++)
                {
                    IRfcStructure oRow = options.Metadata.LineType.CreateStructure();
                    oRow.SetValue("TEXT", (ci == 0 ? "" : "OR ") + $"VBELN = '{chunk[ci]}'");
                    options.Append(oRow);
                }

                IRfcTable fields = rfcVbuk.GetTable("FIELDS");
                string[] fNames = { "VBELN", "WBSTK" };
                foreach (var f in fNames)
                {
                    IRfcStructure fRow = fields.Metadata.LineType.CreateStructure();
                    fRow.SetValue("FIELDNAME", f);
                    fields.Append(fRow);
                }

                rfcVbuk.Invoke(destination);
                IRfcTable data = rfcVbuk.GetTable("DATA");
                for (int j = 0; j < data.RowCount; j++)
                {
                    string raw = data[j].GetString("WA");
                    string[] parts = raw.Split('|');
                    if (parts.Length >= 2)
                    {
                        string v = parts[0].Trim().PadLeft(10, '0');
                        string wbstk = parts[1].Trim();
                        dict[v] = wbstk;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Error consultando lote VBUK: {Msg}", ex.Message);
            }
        }
        return dict;
    }

    private static bool TieneSalidaMercanciasPendiente(
        RfcDestination destination, RfcRepository repository, string numeroEntrega)
    {
        var consulta = repository.CreateFunction("RFC_READ_TABLE");
        consulta.SetValue("QUERY_TABLE", "VBUK");
        consulta.SetValue("DELIMITER", "|");
        consulta.SetValue("ROWCOUNT", 1);
        var opciones = consulta.GetTable("OPTIONS");
        var filtro = opciones.Metadata.LineType.CreateStructure();
        filtro.SetValue("TEXT", $"VBELN = '{numeroEntrega}' AND WBSTK = 'A'");
        opciones.Append(filtro);
        var campos = consulta.GetTable("FIELDS");
        var campo = campos.Metadata.LineType.CreateStructure();
        campo.SetValue("FIELDNAME", "WBSTK");
        campos.Append(campo);
        consulta.Invoke(destination);
        var datos = consulta.GetTable("DATA");
        return datos.RowCount > 0 && datos[0].GetString("WA").Trim() == "A";
    }

    private void ConsultarPosicionesEntrega(RfcDestination destination, RfcRepository repository, string vbelnFormatted, string centro, SapEntregaSalidaDto entrega)
    {
        try
        {
            IRfcFunction rfcLips = repository.CreateFunction("RFC_READ_TABLE");
            rfcLips.SetValue("QUERY_TABLE", "LIPS");
            rfcLips.SetValue("DELIMITER", "|");

            IRfcTable options = rfcLips.GetTable("OPTIONS");
            IRfcStructure optRow = options.Metadata.LineType.CreateStructure();
            optRow.SetValue("TEXT", $"VBELN = '{vbelnFormatted}'");
            options.Append(optRow);
            if (entrega.Canal == "FP")
            {
                var filtroCanal = options.Metadata.LineType.CreateStructure();
                filtroCanal.SetValue("TEXT", " AND VTWEG = '65'");
                options.Append(filtroCanal);
            }

            IRfcTable fields = rfcLips.GetTable("FIELDS");
            string[] fNames = { "POSNR", "MATNR", "ARKTX", "LFIMG", "LGMNG", "VRKME", "WERKS", "LGORT", "CHARG", "VGBEL" };
            foreach (var f in fNames)
            {
                IRfcStructure fRow = fields.Metadata.LineType.CreateStructure();
                fRow.SetValue("FIELDNAME", f);
                fields.Append(fRow);
            }

            rfcLips.Invoke(destination);

            IRfcTable data = rfcLips.GetTable("DATA");
            for (int j = 0; j < data.RowCount; j++)
            {
                string line = data[j].GetString("WA");
                string[] cols = line.Split('|');
                if (cols.Length >= 4)
                {
                    int.TryParse(cols[0].Trim(), out int pos);
                    string matnr = cols[1].Trim().TrimStart('0');
                    string arktx = cols.Length > 2 ? cols[2].Trim() : "";
                    decimal.TryParse(cols.Length > 3 ? cols[3].Trim() : "0", NumberStyles.Any, CultureInfo.InvariantCulture, out decimal cant);
                    
                    // Si LFIMG viene en 0.000 (común en psicotrópicos o partición de lotes), verificar LGMNG
                    if (cant <= 0 && cols.Length > 4)
                    {
                        decimal.TryParse(cols[4].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out cant);
                    }
                    // Si aún así es 0 o menor, asegurar mínimo 1 para no descartar la entrega de SAP
                    if (cant <= 0)
                    {
                        cant = 1;
                    }

                    string meins = cols.Length > 5 ? cols[5].Trim() : "UN";
                    string werks = cols.Length > 6 ? cols[6].Trim() : centro;
                    string lgort = cols.Length > 7 ? cols[7].Trim() : "6001";
                    string charg = cols.Length > 8 ? cols[8].Trim() : "";
                    string vgbel = cols.Length > 9 ? cols[9].Trim().TrimStart('0') : "";

                    if (!string.IsNullOrEmpty(vgbel))
                    {
                        entrega.DocModelo = vgbel;
                        entrega.PedidoVenta = vgbel;
                    }

                    entrega.Lineas.Add(new SapEntregaSalidaItemDto
                    {
                        Posicion = pos > 0 ? pos : (j + 1) * 10,
                        CodigoMaterial = matnr,
                        DescripcionMaterial = string.IsNullOrWhiteSpace(arktx) ? $"MATERIAL {matnr}" : arktx,
                        CantidadSolicitada = cant,
                        CantidadPickeada = 0,
                        UnidadMedida = string.IsNullOrEmpty(meins) ? "UN" : meins,
                        Lote = !string.IsNullOrEmpty(charg) ? charg : null,
                        Centro = string.IsNullOrEmpty(werks) ? centro : werks,
                        Almacen = string.IsNullOrEmpty(lgort) ? "6001" : lgort,
                        UbicacionSugerida = $"RACK-A-{((j + 1) * 2):D2}-01",
                        DocModelo = !string.IsNullOrEmpty(vgbel) ? vgbel : entrega.PedidoVenta,
                        EstadoItem = "PENDIENTE"
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error al consultar LIPS para entrega {Entrega}", vbelnFormatted);
            throw;
        }
    }

    private void GenerarEntregasSimuladas(ConsultaEntregasResponseDto response, DateTime fechaDesde, DateTime fechaHasta)
    {
        // Catálogo de entregas oficiales representativas dentro de la ventana de fechas
        var catalogo = new List<SapEntregaSalidaDto>
        {
            new()
            {
                NumeroEntrega = "8001092831",
                PedidoVenta = "4500981240",
                Destinatario = "HOSPITAL SAN JUAN DE DIOS",
                DireccionDestino = "HUÉRFANOS 3255",
                Comuna = "QUINTA NORMAL",
                Prioridad = "URGENTE",
                FechaCreacion = new DateTime(2026, 7, 28, 9, 30, 0),
                FechaCompromiso = new DateTime(2026, 7, 30, 18, 0, 0),
                Estado = "PENDIENTE_PICKING",
                Lineas = new()
                {
                    new() { Posicion = 10, CodigoMaterial = "500010065", DescripcionMaterial = "PARACETAMOL 500 MG COMPRIMIDOS", CantidadSolicitada = 500, UnidadMedida = "CJ", UbicacionSugerida = "RACK-A-04-02" },
                    new() { Posicion = 20, CodigoMaterial = "500010080", DescripcionMaterial = "IBUPROFENO 400 MG COMPRIMIDOS", CantidadSolicitada = 300, UnidadMedida = "CJ", UbicacionSugerida = "RACK-B-01-05" }
                }
            },
            new()
            {
                NumeroEntrega = "8001092832",
                PedidoVenta = "4500981245",
                Destinatario = "COMPLEJO ASISTENCIAL BARROS LUCO",
                DireccionDestino = "GRAN AVENIDA JOSÉ MIGUEL CARRERA 3204",
                Comuna = "SAN MIGUEL",
                Prioridad = "NORMAL",
                FechaCreacion = new DateTime(2026, 7, 26, 11, 20, 0),
                FechaCompromiso = new DateTime(2026, 7, 29, 12, 0, 0),
                Estado = "EN_PICKING",
                Lineas = new()
                {
                    new() { Posicion = 10, CodigoMaterial = "500010065", DescripcionMaterial = "PARACETAMOL 500 MG COMPRIMIDOS", CantidadSolicitada = 1200, UnidadMedida = "CJ", UbicacionSugerida = "RACK-A-04-02" },
                    new() { Posicion = 20, CodigoMaterial = "500010115", DescripcionMaterial = "AMOXICILINA 500 MG CAPSULAS", CantidadSolicitada = 800, UnidadMedida = "CJ", UbicacionSugerida = "RACK-C-02-01" }
                }
            },
            new()
            {
                NumeroEntrega = "8001092830",
                PedidoVenta = "4500981190",
                Destinatario = "HOSPITAL DR. SÓTERO DEL RÍO",
                DireccionDestino = "AV. CONCHA Y TORO 3459",
                Comuna = "PUENTE ALTO",
                Prioridad = "ALTA",
                FechaCreacion = new DateTime(2026, 7, 20, 15, 10, 0),
                FechaCompromiso = new DateTime(2026, 7, 22, 17, 30, 0),
                Estado = "PICKING_CONFIRMADO",
                FolioDte = "DTE-52-94821",
                Lineas = new()
                {
                    new() { Posicion = 10, CodigoMaterial = "500010200", DescripcionMaterial = "INSULINA HUMANA NPH 100 UI/ML 10 ML", CantidadSolicitada = 250, UnidadMedida = "FCO", UbicacionSugerida = "CAM-FRIO-01" }
                }
            },
            new()
            {
                NumeroEntrega = "8001092825",
                PedidoVenta = "4500981020",
                Destinatario = "HOSPITAL FÉLIX BULNES CERDA",
                DireccionDestino = "MAPOCHO 7432",
                Comuna = "CERRO NAVIA",
                Prioridad = "NORMAL",
                FechaCreacion = new DateTime(2026, 7, 15, 14, 0, 0),
                FechaCompromiso = new DateTime(2026, 7, 18, 10, 0, 0),
                Estado = "DESPACHADO",
                FolioDte = "DTE-52-94710",
                DocumentoSapPgi = "4909800120",
                Lineas = new()
                {
                    new() { Posicion = 10, CodigoMaterial = "500010065", DescripcionMaterial = "PARACETAMOL 500 MG COMPRIMIDOS", CantidadSolicitada = 1000, UnidadMedida = "CJ", UbicacionSugerida = "RACK-A-04-02" }
                }
            },
            new()
            {
                NumeroEntrega = "8001092840",
                PedidoVenta = "4500981310",
                Destinatario = "INSTITUTO NACIONAL DEL TÓRAX",
                DireccionDestino = "J.M. INFANTE 717",
                Comuna = "PROVIDENCIA",
                Prioridad = "URGENTE",
                FechaCreacion = new DateTime(2026, 7, 10, 8, 45, 0),
                FechaCompromiso = new DateTime(2026, 7, 12, 11, 0, 0),
                Estado = "PENDIENTE_PICKING",
                Lineas = new()
                {
                    new() { Posicion = 10, CodigoMaterial = "500010250", DescripcionMaterial = "CEFTRIAXONA 1 G POLVO PARA SOLUCIÓN INYECTABLE", CantidadSolicitada = 600, UnidadMedida = "VIAL", UbicacionSugerida = "RACK-B-03-01" },
                    new() { Posicion = 20, CodigoMaterial = "500010260", DescripcionMaterial = "CLORURO DE SODIO 0.9% 500 ML MATRAZ", CantidadSolicitada = 1500, UnidadMedida = "UN", UbicacionSugerida = "RACK-D-01-02" }
                }
            }
        };

        // Filtramos por el rango de fechas pedido
        var filtradas = catalogo.Where(e => e.FechaCreacion >= fechaDesde && e.FechaCreacion <= fechaHasta).ToList();
        response.Entregas.AddRange(filtradas);
    }

    public async Task<SapMovimientoResponseDto> ContabilizarSalidaMercanciasAsync(ConfirmacionDespachoRequestDto request)
    {
        var response = new SapMovimientoResponseDto();

        try
        {
            var destination = _connectionFactory.GetDestination();
            RfcSessionManager.BeginContext(destination);

            try
            {
                var repository = destination.Repository;

                // Llamada a WS_DELIVERY_UPDATE para contabilizar Post Goods Issue (Mov. 601)
                IRfcFunction deliveryUpdate = repository.CreateFunction("WS_DELIVERY_UPDATE");
                
                var vbelnFormateado = request.NumeroEntregaSap.Trim().PadLeft(10, '0');

                // 0. Verificar si la entrega ya posee fecha real de salida (WADAT_IST) en SAP
                try
                {
                    IRfcFunction rfcLikp = repository.CreateFunction("RFC_READ_TABLE");
                    rfcLikp.SetValue("QUERY_TABLE", "LIKP");
                    rfcLikp.SetValue("DELIMITER", "|");
                    IRfcTable lOpt = rfcLikp.GetTable("OPTIONS");
                    IRfcStructure loRow = lOpt.Metadata.LineType.CreateStructure();
                    loRow.SetValue("TEXT", $"VBELN = '{vbelnFormateado}'");
                    lOpt.Append(loRow);
                    IRfcTable lFlds = rfcLikp.GetTable("FIELDS");
                    IRfcStructure lfRow = lFlds.Metadata.LineType.CreateStructure();
                    lfRow.SetValue("FIELDNAME", "WADAT_IST");
                    lFlds.Append(lfRow);
                    rfcLikp.Invoke(destination);
                    IRfcTable lData = rfcLikp.GetTable("DATA");
                    if (lData.RowCount > 0)
                    {
                        string wadat = lData[0].GetString("WA").Trim();
                        if (!string.IsNullOrWhiteSpace(wadat) && wadat != "00000000")
                        {
                            _logger.LogInformation("Entrega {Vbeln} posee fecha WADAT_IST {Wadat} en SAP. Verificando si existe Documento de Material (Mov. 601) en VBFA...", vbelnFormateado, wadat);
                            var docMat = ObtenerDocumentoMaterialDesdeVbfa(destination, repository, vbelnFormateado);
                            if (!string.IsNullOrWhiteSpace(docMat))
                            {
                                _logger.LogInformation("Entrega {Vbeln} ya se encuentra efectivamente contabilizada en SAP con Doc. Material {DocMat}.", vbelnFormateado, docMat);
                                response.Exitoso = true;
                                response.DocumentoMaterial = docMat;
                                response.Mensajes.Add(new BapiReturnMessageDto
                                {
                                    Tipo = "S",
                                    Mensaje = $"La entrega {request.NumeroEntregaSap} ya se encuentra contabilizada en SAP con fecha {wadat} (Doc. Material: {docMat})."
                                });
                                return await Task.FromResult(response);
                            }
                            else
                            {
                                _logger.LogInformation("Entrega {Vbeln} tiene WADAT_IST {Wadat} pero NO tiene Documento de Material en VBFA (Salida de mercancías incompleta/pendiente). Continuando con proceso de contabilización...", vbelnFormateado, wadat);
                            }
                        }
                    }
                }
                catch { }

                // 1. Consultar datos de LIPS de la entrega para obtener WERKS, LGORT, CHARG, VRKME y CHSPL (partición de lotes)
                var lipsPosiciones = new Dictionary<string, (string werks, string lgort, string charg, string vrkme, decimal lfimg, string chspl)>();
                try
                {
                    IRfcFunction rfcLips = repository.CreateFunction("RFC_READ_TABLE");
                    rfcLips.SetValue("QUERY_TABLE", "LIPS");
                    rfcLips.SetValue("DELIMITER", "|");
                    IRfcTable opt = rfcLips.GetTable("OPTIONS");
                    IRfcStructure oRow = opt.Metadata.LineType.CreateStructure();
                    oRow.SetValue("TEXT", $"VBELN = '{vbelnFormateado}'");
                    opt.Append(oRow);
                    IRfcTable flds = rfcLips.GetTable("FIELDS");
                    foreach (var f in new[] { "POSNR", "WERKS", "LGORT", "CHARG", "VRKME", "LFIMG", "CHSPL" })
                    {
                        IRfcStructure fRow = flds.Metadata.LineType.CreateStructure();
                        fRow.SetValue("FIELDNAME", f);
                        flds.Append(fRow);
                    }
                    rfcLips.Invoke(destination);
                    IRfcTable data = rfcLips.GetTable("DATA");
                    for (int k = 0; k < data.RowCount; k++)
                    {
                        string[] p = data[k].GetString("WA").Split('|');
                        if (p.Length >= 5)
                        {
                            decimal.TryParse(p.Length > 5 ? p[5].Trim() : "0", NumberStyles.Any, CultureInfo.InvariantCulture, out decimal cantLips);
                            string chspl = p.Length > 6 ? p[6].Trim() : "";
                            lipsPosiciones[p[0].Trim().PadLeft(6, '0')] = (p[1].Trim(), p[2].Trim(), p[3].Trim(), p[4].Trim(), cantLips, chspl);
                        }
                    }
                }
                catch (Exception exLips)
                {
                    _logger.LogWarning(exLips, "No se pudo pre-consultar LIPS para entrega {Vbeln}", vbelnFormateado);
                }

                // PASO 0: Crear la partición de lotes en la entrega mediante BAPI_OUTB_DELIVERY_CHANGE
                // Regla SAP: La posición madre (000010) debe quedar con cantidad 0 y la subposición (900001) creada con lote y cantidad.
                try
                {
                    int splitCounter = 900001;
                    bool tieneItemsParaSplit = false;

                    IRfcFunction bapiChange = repository.CreateFunction("BAPI_OUTB_DELIVERY_CHANGE");
                    IRfcStructure headerData = bapiChange.GetStructure("HEADER_DATA");
                    headerData.SetValue("DELIV_NUMB", vbelnFormateado);
                    IRfcStructure headerControl = bapiChange.GetStructure("HEADER_CONTROL");
                    headerControl.SetValue("DELIV_NUMB", vbelnFormateado);
                    IRfcStructure technControl = bapiChange.GetStructure("TECHN_CONTROL");
                    technControl.SetValue("UPD_IND", "U");

                    IRfcTable itemData = bapiChange.GetTable("ITEM_DATA");
                    IRfcTable itemControl = bapiChange.GetTable("ITEM_CONTROL");

                    foreach (var item in request.Items)
                    {
                        string posFmt = item.Posicion.ToString().PadLeft(6, '0');
                        string matnrPadded = item.CodigoMaterial.Trim().PadLeft(18, '0');
                        string lote = item.LoteSap?.Trim() ?? string.Empty;
                        decimal cant = item.CantidadDespachada;

                        string chargLips = "";
                        string chspl = "";
                        if (lipsPosiciones.TryGetValue(posFmt, out var lp))
                        {
                            chargLips = lp.charg;
                            chspl = lp.chspl;
                            if (lp.lfimg > 0 && cant > lp.lfimg) cant = lp.lfimg;
                        }

                        string? existingSplit = lipsPosiciones.Keys
                            .FirstOrDefault(k => int.TryParse(k, out int p) && p >= 900000 && lipsPosiciones[k].charg == lote);

                        bool necesitaSplit = (!string.IsNullOrWhiteSpace(lote) && string.IsNullOrWhiteSpace(chargLips))
                            || chspl == "X";

                        if (necesitaSplit && string.IsNullOrWhiteSpace(existingSplit) && !string.IsNullOrWhiteSpace(lote))
                        {
                            tieneItemsParaSplit = true;
                            string subposStr = splitCounter.ToString().PadLeft(6, '0');
                            splitCounter++;

                            // 1. Reducir la posición principal a 0 de entrega
                            IRfcStructure itMain = itemData.Metadata.LineType.CreateStructure();
                            itMain.SetValue("DELIV_NUMB", vbelnFormateado);
                            itMain.SetValue("DELIV_ITEM", posFmt);
                            itMain.SetValue("MATERIAL", matnrPadded);
                            itMain.SetValue("DLV_QTY", 0);
                            itMain.SetValue("DLV_QTY_IMUNIT", 0);
                            itMain.SetValue("FACT_UNIT_NOM", 1);
                            itMain.SetValue("FACT_UNIT_DENOM", 1);
                            itemData.Append(itMain);

                            IRfcStructure ctMain = itemControl.Metadata.LineType.CreateStructure();
                            ctMain.SetValue("DELIV_NUMB", vbelnFormateado);
                            ctMain.SetValue("DELIV_ITEM", posFmt);
                            ctMain.SetValue("CHG_DELQTY", "X");
                            itemControl.Append(ctMain);

                            // 2. Crear subposición con el lote y la cantidad
                            IRfcStructure itSplit = itemData.Metadata.LineType.CreateStructure();
                            itSplit.SetValue("DELIV_NUMB", vbelnFormateado);
                            itSplit.SetValue("DELIV_ITEM", subposStr);
                            itSplit.SetValue("MATERIAL", matnrPadded);
                            itSplit.SetValue("BATCH", lote);
                            itSplit.SetValue("DLV_QTY", cant);
                            itSplit.SetValue("DLV_QTY_IMUNIT", cant);
                            itSplit.SetValue("HIERARITEM", posFmt);
                            itSplit.SetValue("USEHIERITM", "1");
                            itSplit.SetValue("FACT_UNIT_NOM", 1);
                            itSplit.SetValue("FACT_UNIT_DENOM", 1);
                            itemData.Append(itSplit);

                            IRfcStructure ctSplit = itemControl.Metadata.LineType.CreateStructure();
                            ctSplit.SetValue("DELIV_NUMB", vbelnFormateado);
                            ctSplit.SetValue("DELIV_ITEM", subposStr);
                            ctSplit.SetValue("CHG_DELQTY", "X");
                            itemControl.Append(ctSplit);
                        }
                    }

                    if (tieneItemsParaSplit)
                    {
                        _logger.LogInformation("Creando partición de lotes estructural en SAP para entrega {Vbeln} con BAPI_OUTB_DELIVERY_CHANGE...", vbelnFormateado);
                        bapiChange.Invoke(destination);

                        IRfcFunction commitBapi = repository.CreateFunction("BAPI_TRANSACTION_COMMIT");
                        commitBapi.SetValue("WAIT", "X");
                        commitBapi.Invoke(destination);

                        // Re-consultar LIPS para refrescar las subposiciones generadas
                        lipsPosiciones.Clear();
                        IRfcFunction rfcLipsReload = repository.CreateFunction("RFC_READ_TABLE");
                        rfcLipsReload.SetValue("QUERY_TABLE", "LIPS");
                        rfcLipsReload.SetValue("DELIMITER", "|");
                        IRfcTable optReload = rfcLipsReload.GetTable("OPTIONS");
                        IRfcStructure oRowReload = optReload.Metadata.LineType.CreateStructure();
                        oRowReload.SetValue("TEXT", $"VBELN = '{vbelnFormateado}'");
                        optReload.Append(oRowReload);
                        IRfcTable fldsReload = rfcLipsReload.GetTable("FIELDS");
                        foreach (var f in new[] { "POSNR", "WERKS", "LGORT", "CHARG", "VRKME", "LFIMG", "CHSPL" })
                        {
                            IRfcStructure fRow = fldsReload.Metadata.LineType.CreateStructure();
                            fRow.SetValue("FIELDNAME", f);
                            fldsReload.Append(fRow);
                        }
                        rfcLipsReload.Invoke(destination);
                        IRfcTable dataReload = rfcLipsReload.GetTable("DATA");
                        for (int k = 0; k < dataReload.RowCount; k++)
                        {
                            string[] p = dataReload[k].GetString("WA").Split('|');
                            if (p.Length >= 5)
                            {
                                decimal.TryParse(p.Length > 5 ? p[5].Trim() : "0", NumberStyles.Any, CultureInfo.InvariantCulture, out decimal cantLips);
                                string chsplReload = p.Length > 6 ? p[6].Trim() : "";
                                lipsPosiciones[p[0].Trim().PadLeft(6, '0')] = (p[1].Trim(), p[2].Trim(), p[3].Trim(), p[4].Trim(), cantLips, chsplReload);
                            }
                        }
                    }
                }
                catch (Exception exBapi)
                {
                    _logger.LogWarning(exBapi, "Error o función BAPI_OUTB_DELIVERY_CHANGE no aplicable para entrega {Entrega}", vbelnFormateado);
                }

                // PASO 1: Actualizar picking y lotes en la entrega (sin contabilizar aún WABUC)
                IRfcFunction step1Update = repository.CreateFunction("WS_DELIVERY_UPDATE");
                step1Update.SetValue("DELIVERY", vbelnFormateado);
                step1Update.SetValue("UPDATE_PICKING", "X");
                step1Update.SetValue("SYNCHRON", "X");
                step1Update.SetValue("COMMIT", "X");
                try { step1Update.SetValue("IF_DATABASE_UPDATE", "1"); } catch { }
                try { step1Update.SetValue("IF_ERROR_MESSAGES_SEND_0", "X"); } catch { }

                IRfcStructure vbkok1 = step1Update.GetStructure("VBKOK_WA");
                vbkok1.SetValue("VBELN_VL", vbelnFormateado);
                try { vbkok1.SetValue("VBELN", vbelnFormateado); } catch { }
                try { vbkok1.SetValue("KOMUE", "X"); } catch { }
                try { vbkok1.SetValue("KZEFF", "X"); } catch { }

                IRfcTable vbpok = step1Update.GetTable("VBPOK_TAB");
                int nextSplitPos = 900001;
                foreach (var item in request.Items)
                {
                    string posFmt = item.Posicion.ToString().PadLeft(6, '0');
                    string werks = "6000";
                    string lgort = "6101";
                    string chargOriginalLips = "";
                    string vrkmeLips = "";
                    string chsplLips = "";
                    decimal cantAjustada = item.CantidadDespachada;

                    if (lipsPosiciones.TryGetValue(posFmt, out var lp))
                    {
                        if (!string.IsNullOrWhiteSpace(lp.werks)) werks = lp.werks;
                        if (!string.IsNullOrWhiteSpace(lp.lgort)) lgort = lp.lgort;
                        chargOriginalLips = lp.charg;
                        vrkmeLips = lp.vrkme;
                        chsplLips = lp.chspl;
                        if (lp.lfimg > 0 && cantAjustada > lp.lfimg)
                        {
                            cantAjustada = lp.lfimg;
                        }
                    }

                    string matnrPadded = item.CodigoMaterial.Trim().PadLeft(18, '0');
                    string loteFinal = !string.IsNullOrWhiteSpace(chargOriginalLips) ? chargOriginalLips : (item.LoteSap?.Trim() ?? string.Empty);

                    try
                    {
                        IRfcFunction rfcMchb = repository.CreateFunction("RFC_READ_TABLE");
                        rfcMchb.SetValue("QUERY_TABLE", "MCHB");
                        rfcMchb.SetValue("DELIMITER", "|");
                        IRfcTable mOpt = rfcMchb.GetTable("OPTIONS");
                        IRfcStructure mRow = mOpt.Metadata.LineType.CreateStructure();
                        mRow.SetValue("TEXT", $"MATNR = '{matnrPadded}' AND WERKS = '{werks}' AND LGORT = '{lgort}'");
                        mOpt.Append(mRow);
                        IRfcTable mFlds = rfcMchb.GetTable("FIELDS");
                        foreach (var f in new[] { "CHARG", "CLABS" })
                        {
                            IRfcStructure fRow = mFlds.Metadata.LineType.CreateStructure();
                            fRow.SetValue("FIELDNAME", f);
                            mFlds.Append(fRow);
                        }
                        rfcMchb.Invoke(destination);
                        IRfcTable mData = rfcMchb.GetTable("DATA");
                        var lotesConStock = new List<string>();
                        for (int m = 0; m < mData.RowCount; m++)
                        {
                            string[] mp = mData[m].GetString("WA").Split('|');
                            if (mp.Length >= 2 && !string.IsNullOrWhiteSpace(mp[0]))
                            {
                                decimal.TryParse(mp[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal stock);
                                if (stock > 0)
                                {
                                    lotesConStock.Add(mp[0].Trim());
                                }
                            }
                        }

                        if (lotesConStock.Count > 0)
                        {
                            if (string.IsNullOrWhiteSpace(loteFinal) || !lotesConStock.Contains(loteFinal))
                            {
                                _logger.LogInformation("Lote '{LoteEnviado}' no válido o sin stock en SAP. Usando lote con stock disponible: '{LoteReal}'",
                                    loteFinal, lotesConStock[0]);
                                loteFinal = lotesConStock[0];
                            }
                        }
                    }
                    catch (Exception exMchb)
                    {
                        _logger.LogWarning(exMchb, "No se pudo consultar stock MCHB para material {Matnr}", matnrPadded);
                    }

                    // Priorizar la unidad interna nativa de SAP leída desde LIPS (ej. KI, ST, UN)
                    string uom = !string.IsNullOrWhiteSpace(vrkmeLips) ? vrkmeLips : (item.UnidadMedida?.Trim() ?? string.Empty);
                    if (uom.Equals("CAJ", StringComparison.OrdinalIgnoreCase)) uom = "KI";
                    if (uom.Equals("UN", StringComparison.OrdinalIgnoreCase) || uom.Equals("UND", StringComparison.OrdinalIgnoreCase)) uom = "ST";

                    // Verificar si en LIPS ya existe una subposición previa (>= 900001) para este lote/material
                    string? existingSplitPos = lipsPosiciones.Keys
                        .FirstOrDefault(k => int.TryParse(k, out int p) && p >= 900000 && lipsPosiciones[k].charg == loteFinal);

                    // Determinar si la posición en SAP requiere partición de lotes:
                    // 1. Si chsplLips == 'X'
                    // 2. Si se asigna un lote y la posición principal en SAP aún no tenía lote fijado (flujo estándar SD)
                    bool requiereSplit = (!string.IsNullOrWhiteSpace(loteFinal) && string.IsNullOrWhiteSpace(chargOriginalLips))
                        || chsplLips == "X"
                        || !string.IsNullOrWhiteSpace(existingSplitPos);

                    if (requiereSplit && !string.IsNullOrWhiteSpace(loteFinal))
                    {
                        string subposStr = !string.IsNullOrWhiteSpace(existingSplitPos)
                            ? existingSplitPos
                            : nextSplitPos.ToString().PadLeft(6, '0');

                        if (string.IsNullOrWhiteSpace(existingSplitPos))
                        {
                            nextSplitPos++;
                        }

                        _logger.LogInformation("Posición {Pos} de entrega {Vbeln}: Generando estructura VBPOK con madre 0 y subposición {Subpos} con lote '{Lote}', cantidad {Cant} {Uom}.",
                            posFmt, vbelnFormateado, subposStr, loteFinal, cantAjustada, uom);

                        // Regla SAP oficial para partición de lotes en VBPOK_TAB:
                        // Fila 1: Posición madre (000010) DEBE llevar PIKMG = 0 para no exigir lote directo.
                        IRfcStructure rowMain = vbpok.Metadata.LineType.CreateStructure();
                        rowMain.SetValue("VBELN_VL", vbelnFormateado);
                        rowMain.SetValue("POSNR_VL", posFmt); // "000010"
                        rowMain.SetValue("VBELN", vbelnFormateado);
                        rowMain.SetValue("POSNN", posFmt);    // "000010"
                        rowMain.SetValue("MATNR", matnrPadded);
                        rowMain.SetValue("WERKS", werks);
                        rowMain.SetValue("LGORT", lgort);
                        rowMain.SetValue("PIKMG", 0m); // Cero en la posición principal
                        if (!string.IsNullOrWhiteSpace(uom)) { try { rowMain.SetValue("VRKME", uom); } catch { } }
                        try { rowMain.SetValue("LIANP", "X"); } catch { }
                        vbpok.Append(rowMain);

                        // Fila 2: Subposición de partición (900001) con lote, cantidad pickeada y confirmación TAQUI
                        IRfcStructure rowSplit = vbpok.Metadata.LineType.CreateStructure();
                        rowSplit.SetValue("VBELN_VL", vbelnFormateado);
                        rowSplit.SetValue("POSNR_VL", !string.IsNullOrWhiteSpace(existingSplitPos) ? subposStr : posFmt);
                        rowSplit.SetValue("VBELN", vbelnFormateado);
                        rowSplit.SetValue("POSNN", subposStr); // Subposición 900001
                        try { rowSplit.SetValue("ORPOS", posFmt); } catch { }
                        rowSplit.SetValue("MATNR", matnrPadded);
                        rowSplit.SetValue("CHARG", loteFinal);
                        rowSplit.SetValue("WERKS", werks);
                        rowSplit.SetValue("LGORT", lgort);
                        try { rowSplit.SetValue("LFIMG", cantAjustada); } catch { }
                        try { rowSplit.SetValue("LGMNG", cantAjustada); } catch { }
                        rowSplit.SetValue("PIKMG", cantAjustada);
                        if (!string.IsNullOrWhiteSpace(uom)) { try { rowSplit.SetValue("VRKME", uom); } catch { } }
                        try { rowSplit.SetValue("LIANP", "X"); } catch { }
                        try { rowSplit.SetValue("TAQUI", "X"); } catch { }
                        vbpok.Append(rowSplit);
                    }
                    else
                    {
                        // Posición estándar (sin lote o con lote pre-existente en cabecera de posición)
                        IRfcStructure row = vbpok.Metadata.LineType.CreateStructure();
                        row.SetValue("VBELN_VL", vbelnFormateado);
                        row.SetValue("POSNR_VL", posFmt);
                        row.SetValue("VBELN", vbelnFormateado);
                        row.SetValue("POSNN", posFmt);
                        row.SetValue("MATNR", matnrPadded);
                        if (!string.IsNullOrWhiteSpace(loteFinal))
                        {
                            row.SetValue("CHARG", loteFinal);
                        }
                        row.SetValue("WERKS", werks);
                        row.SetValue("LGORT", lgort);
                        row.SetValue("PIKMG", cantAjustada);
                        // Solo adaptar cantidad si el pickeo fue parcial inferior a lo solicitado
                        if (lp.lfimg > 0 && cantAjustada < lp.lfimg)
                        {
                            try { row.SetValue("LIANP", "X"); } catch { }
                        }
                        if (!string.IsNullOrWhiteSpace(uom))
                        {
                            try { row.SetValue("VRKME", uom); } catch { }
                        }
                        vbpok.Append(row);
                    }
                }

                step1Update.Invoke(destination);

                // Validar mensajes del Paso 1
                IRfcTable protTable1 = step1Update.GetTable("PROT");
                bool tieneErroresPaso1 = false;
                for (int i = 0; i < protTable1.RowCount; i++)
                {
                    var r = protTable1[i];
                    var msgty = r.GetString("MSGTY");
                    string msgText = "";
                    try { msgText = r.GetString("MESSAGE"); } catch { }
                    if (string.IsNullOrWhiteSpace(msgText))
                    {
                        var v1 = r.GetString("MSGV1");
                        var v2 = r.GetString("MSGV2");
                        var v3 = r.GetString("MSGV3");
                        var v4 = r.GetString("MSGV4");
                        msgText = $"{v1} {v2} {v3} {v4}".Trim();
                    }
                    if (string.IsNullOrWhiteSpace(msgText))
                    {
                        msgText = $"Mensaje SAP ({r.GetString("MSGID")} {r.GetString("MSGNO")})";
                    }

                    response.Mensajes.Add(new BapiReturnMessageDto
                    {
                        Tipo = msgty,
                        Mensaje = msgText,
                        IdMensaje = r.GetString("MSGID"),
                        NumeroMensaje = r.GetString("MSGNO"),
                        Variable1 = r.GetString("MSGV1"),
                        Variable2 = r.GetString("MSGV2"),
                        Variable3 = r.GetString("MSGV3"),
                        Variable4 = r.GetString("MSGV4")
                    });

                    bool esYaContabilizada = (r.GetString("MSGID") == "VL" && (r.GetString("MSGNO") == "602" || r.GetString("MSGNO") == "049" || r.GetString("MSGNO") == "216"))
                        || msgText.ToLower().Contains("salida de mercancía contabilizada")
                        || msgText.ToLower().Contains("ya contabilizada");

                    if ((msgty == "E" || msgty == "A") && !esYaContabilizada)
                    {
                        tieneErroresPaso1 = true;
                    }
                }

                if (tieneErroresPaso1)
                {
                    // Si SAP reporta incompletitud (VU 010), consultar VBUV para obtener el campo técnico exacto faltante
                    try
                    {
                        IRfcFunction rfcVbuv = repository.CreateFunction("RFC_READ_TABLE");
                        rfcVbuv.SetValue("QUERY_TABLE", "VBUV");
                        rfcVbuv.SetValue("DELIMITER", "|");
                        IRfcTable vOpt = rfcVbuv.GetTable("OPTIONS");
                        IRfcStructure voRow = vOpt.Metadata.LineType.CreateStructure();
                        voRow.SetValue("TEXT", $"VBELN = '{vbelnFormateado}'");
                        vOpt.Append(voRow);
                        IRfcTable vFlds = rfcVbuv.GetTable("FIELDS");
                        foreach (var f in new[] { "POSNR", "TBMSK", "FDNAM" })
                        {
                            IRfcStructure fRow = vFlds.Metadata.LineType.CreateStructure();
                            fRow.SetValue("FIELDNAME", f);
                            vFlds.Append(fRow);
                        }
                        rfcVbuv.Invoke(destination);
                        IRfcTable vData = rfcVbuv.GetTable("DATA");
                        for (int v = 0; v < vData.RowCount; v++)
                        {
                            string[] vp = vData[v].GetString("WA").Split('|');
                            if (vp.Length >= 3)
                            {
                                string posIncomp = vp[0].Trim();
                                string tablaIncomp = vp[1].Trim();
                                string campoIncomp = vp[2].Trim();
                                _logger.LogWarning("SAP VBUV: Incompletitud en Entrega {Entrega}, Posición {Pos}: Campo {Campo} en tabla {Tabla}",
                                    vbelnFormateado, posIncomp, campoIncomp, tablaIncomp);
                                response.Mensajes.Add(new BapiReturnMessageDto
                                {
                                    Tipo = "E",
                                    IdMensaje = "VU",
                                    NumeroMensaje = "010",
                                    Mensaje = $"Incompletitud SAP en Posición {posIncomp}: Campo obligatorio '{campoIncomp}' no establecido en tabla {tablaIncomp}"
                                });
                            }
                        }
                    }
                    catch (Exception exVbuv)
                    {
                        _logger.LogWarning(exVbuv, "No se pudo consultar VBUV para entrega {Entrega}", vbelnFormateado);
                    }

                    response.Exitoso = false;
                    try
                    {
                        IRfcFunction rollback = repository.CreateFunction("BAPI_TRANSACTION_ROLLBACK");
                        rollback.Invoke(destination);
                    }
                    catch { }
                    return await Task.FromResult(response);
                }

                // Commit del Paso 1 (guardar picking y lote fijado en SAP)
                IRfcFunction commitStep1 = repository.CreateFunction("BAPI_TRANSACTION_COMMIT");
                commitStep1.SetValue("WAIT", "X");
                commitStep1.Invoke(destination);

                // PASO 2: Contabilizar Salida de Mercancías (PGI / Movimiento 601)
                IRfcFunction step2Pgi = repository.CreateFunction("WS_DELIVERY_UPDATE");
                step2Pgi.SetValue("DELIVERY", vbelnFormateado);
                step2Pgi.SetValue("SYNCHRON", "X");
                step2Pgi.SetValue("COMMIT", "X");
                try { step2Pgi.SetValue("IF_DATABASE_UPDATE", "1"); } catch { }
                try { step2Pgi.SetValue("IF_ERROR_MESSAGES_SEND_0", "X"); } catch { }

                IRfcStructure vbkok2 = step2Pgi.GetStructure("VBKOK_WA");
                vbkok2.SetValue("VBELN_VL", vbelnFormateado);
                try { vbkok2.SetValue("VBELN", vbelnFormateado); } catch { }
                vbkok2.SetValue("WABUC", "X"); // Contabilizar salida de mercancías (PGI)
                string fechaContab = request.FechaDespacho.ToString("yyyyMMdd");
                vbkok2.SetValue("WADAT_IST", fechaContab);

                step2Pgi.Invoke(destination);

                IRfcTable protTable2 = step2Pgi.GetTable("PROT");
                bool tieneErroresPgi = false;
                bool periodoCerradoM7 = false;
                for (int i = 0; i < protTable2.RowCount; i++)
                {
                    var r = protTable2[i];
                    var msgty = r.GetString("MSGTY");
                    var msgid = r.GetString("MSGID");
                    var msgno = r.GetString("MSGNO");
                    if (msgid == "M7" && msgno == "053")
                    {
                        periodoCerradoM7 = true;
                    }
                }

                // Si SAP reporta período cerrado (M7 053), reintentar automáticamente con último día del período abierto (20261031)
                if (periodoCerradoM7)
                {
                    _logger.LogWarning("SAP reportó período contable cerrado para {Fecha}. Reintentando PGI con último día de período abierto (20261031)...", fechaContab);
                    IRfcFunction retryPgi = repository.CreateFunction("WS_DELIVERY_UPDATE");
                    retryPgi.SetValue("DELIVERY", vbelnFormateado);
                    retryPgi.SetValue("SYNCHRON", "X");
                    retryPgi.SetValue("COMMIT", "X");
                    try { retryPgi.SetValue("IF_DATABASE_UPDATE", "1"); } catch { }
                    try { retryPgi.SetValue("IF_ERROR_MESSAGES_SEND_0", "X"); } catch { }

                    IRfcStructure retryVbkok = retryPgi.GetStructure("VBKOK_WA");
                    retryVbkok.SetValue("VBELN_VL", vbelnFormateado);
                    try { retryVbkok.SetValue("VBELN", vbelnFormateado); } catch { }
                    retryVbkok.SetValue("WABUC", "X");
                    retryVbkok.SetValue("WADAT_IST", "20261031");
                    retryPgi.Invoke(destination);
                    protTable2 = retryPgi.GetTable("PROT");
                }

                for (int i = 0; i < protTable2.RowCount; i++)
                {
                    var r = protTable2[i];
                    var msgty = r.GetString("MSGTY");
                    string msgText = "";
                    try { msgText = r.GetString("MESSAGE"); } catch { }
                    if (string.IsNullOrWhiteSpace(msgText))
                    {
                        var v1 = r.GetString("MSGV1");
                        var v2 = r.GetString("MSGV2");
                        var v3 = r.GetString("MSGV3");
                        var v4 = r.GetString("MSGV4");
                        msgText = $"{v1} {v2} {v3} {v4}".Trim();
                    }
                    if (string.IsNullOrWhiteSpace(msgText))
                    {
                        msgText = $"Mensaje SAP ({r.GetString("MSGID")} {r.GetString("MSGNO")})";
                    }

                    response.Mensajes.Add(new BapiReturnMessageDto
                    {
                        Tipo = msgty,
                        Mensaje = msgText,
                        IdMensaje = r.GetString("MSGID"),
                        NumeroMensaje = r.GetString("MSGNO"),
                        Variable1 = r.GetString("MSGV1"),
                        Variable2 = r.GetString("MSGV2"),
                        Variable3 = r.GetString("MSGV3"),
                        Variable4 = r.GetString("MSGV4")
                    });

                    bool esYaContabilizada = (r.GetString("MSGID") == "VL" && (r.GetString("MSGNO") == "602" || r.GetString("MSGNO") == "049" || r.GetString("MSGNO") == "216"))
                        || msgText.ToLower().Contains("salida de mercancía contabilizada")
                        || msgText.ToLower().Contains("ya contabilizada");

                    if ((msgty == "E" || msgty == "A") && !esYaContabilizada)
                    {
                        tieneErroresPgi = true;
                    }
                }

                if (tieneErroresPgi)
                {
                    response.Exitoso = false;
                    try
                    {
                        IRfcFunction rollback = repository.CreateFunction("BAPI_TRANSACTION_ROLLBACK");
                        rollback.Invoke(destination);
                    }
                    catch { }
                    return await Task.FromResult(response);
                }

                // Commit en SAP tras PGI exitoso
                IRfcFunction commitFunction = repository.CreateFunction("BAPI_TRANSACTION_COMMIT");
                commitFunction.SetValue("WAIT", "X");
                commitFunction.Invoke(destination);

                var docMaterialReal = ObtenerDocumentoMaterialDesdeVbfa(destination, repository, vbelnFormateado);

                // Si no se encontró un documento de material real (en VBFA con tipo R), PGI NO se contabilizó en SAP
                if (string.IsNullOrEmpty(docMaterialReal))
                {
                    string wbstk = "";
                    try
                    {
                        IRfcFunction rfcVbuk = repository.CreateFunction("RFC_READ_TABLE");
                        rfcVbuk.SetValue("QUERY_TABLE", "VBUK");
                        rfcVbuk.SetValue("DELIMITER", "|");
                        IRfcTable vOpt = rfcVbuk.GetTable("OPTIONS");
                        IRfcStructure voRow = vOpt.Metadata.LineType.CreateStructure();
                        voRow.SetValue("TEXT", $"VBELN = '{vbelnFormateado}'");
                        vOpt.Append(voRow);
                        IRfcTable vFlds = rfcVbuk.GetTable("FIELDS");
                        IRfcStructure vfRow = vFlds.Metadata.LineType.CreateStructure();
                        vfRow.SetValue("FIELDNAME", "WBSTK");
                        vFlds.Append(vfRow);
                        rfcVbuk.Invoke(destination);
                        IRfcTable vData = rfcVbuk.GetTable("DATA");
                        if (vData.RowCount > 0)
                        {
                            wbstk = vData[0].GetString("WA").Trim();
                        }
                    }
                    catch { }

                    response.Exitoso = false;
                    response.DocumentoMaterial = null;
                    response.Mensajes.Add(new BapiReturnMessageDto
                    {
                        Tipo = "E",
                        Mensaje = $"El picking se procesó en SAP, pero la Salida de Mercancías (PGI Mov. 601) NO generó Documento de Material (Estatus SM: '{wbstk}'). Revise en SAP GUI si el material exige 'Partición de lote' o si falta asignación de lote en la posición."
                    });
                    return await Task.FromResult(response);
                }

                response.Exitoso = true;
                response.DocumentoMaterial = docMaterialReal;
                response.Mensajes.Add(new BapiReturnMessageDto
                {
                    Tipo = "S",
                    Mensaje = $"Salida de mercancías confirmada exitosamente en SAP para entrega {request.NumeroEntregaSap} (Doc. Material: {docMaterialReal})."
                });
            }
            finally
            {
                RfcSessionManager.EndContext(destination);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al contabilizar salida de mercancías para entrega {Entrega}", request.NumeroEntregaSap);
            response.Exitoso = false;
            response.Mensajes.Add(new BapiReturnMessageDto { Tipo = "E", Mensaje = ex.Message });
        }

        return await Task.FromResult(response);
    }

    /// <summary>
    /// Consulta el flujo de documentos comerciales (VBFA) en SAP para obtener el número de Documento de Material (Mov. 601) real generado.
    /// </summary>
    private string? ObtenerDocumentoMaterialDesdeVbfa(RfcDestination destination, RfcRepository repository, string vbelnFormatted)
    {
        for (int intento = 1; intento <= 3; intento++)
        {
            try
            {
                IRfcFunction rfcVbfa = repository.CreateFunction("RFC_READ_TABLE");
                rfcVbfa.SetValue("QUERY_TABLE", "VBFA");
                rfcVbfa.SetValue("DELIMITER", "|");
                rfcVbfa.SetValue("ROWCOUNT", 10);

                IRfcTable options = rfcVbfa.GetTable("OPTIONS");
                IRfcStructure optRow = options.Metadata.LineType.CreateStructure();
                optRow.SetValue("TEXT", $"VBELV = '{vbelnFormatted}'");
                options.Append(optRow);

                IRfcTable fields = rfcVbfa.GetTable("FIELDS");
                foreach (var f in new[] { "VBELN", "VBTYP_N" })
                {
                    IRfcStructure fRow = fields.Metadata.LineType.CreateStructure();
                    fRow.SetValue("FIELDNAME", f);
                    fields.Append(fRow);
                }

                rfcVbfa.Invoke(destination);

                IRfcTable data = rfcVbfa.GetTable("DATA");
                string? docFallback = null;
                for (int i = 0; i < data.RowCount; i++)
                {
                    string[] parts = data[i].GetString("WA").Split('|');
                    if (parts.Length >= 2)
                    {
                        string docMat = parts[0].Trim();
                        string vbtyp = parts[1].Trim();
                        // Los documentos de material de salida de mercancías (PGI 601) en SAP comienzan con "49"
                        if (docMat.StartsWith("49"))
                        {
                            _logger.LogInformation("Documento de Material real encontrado en VBFA para entrega {Entrega}: {DocMat} (Tipo {Tipo})", vbelnFormatted, docMat, vbtyp);
                            return docMat;
                        }

                        if (vbtyp == "R" && docFallback == null)
                        {
                            docFallback = docMat;
                        }
                    }
                    else if (parts.Length == 1 && parts[0].Trim().StartsWith("49"))
                    {
                        return parts[0].Trim();
                    }
                }

                if (!string.IsNullOrEmpty(docFallback))
                {
                    _logger.LogInformation("Documento de Material fallback encontrado en VBFA para entrega {Entrega}: {DocMat}", vbelnFormatted, docFallback);
                    return docFallback;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Intento {Intento}: No se pudo consultar tabla VBFA para entrega {Entrega}.", intento, vbelnFormatted);
            }

            if (intento < 3)
            {
                Thread.Sleep(400);
            }
        }

        // Si no se encuentra en VBFA, retornar null (NO inventar número de entrega)
        return null;
    }

    public async Task<object> DebugEntregaAsync(string vbeln)
    {
        var destination = _connectionFactory.GetDestination();
        var repository = destination.Repository;
        string vbelnFmt = vbeln.Trim().PadLeft(10, '0');

        var res = new Dictionary<string, object>();

        Func<string, string[], string, List<Dictionary<string, string>>> queryTable = (tableName, fields, where) =>
        {
            var list = new List<Dictionary<string, string>>();
            try
            {
                IRfcFunction rfc = repository.CreateFunction("RFC_READ_TABLE");
                rfc.SetValue("QUERY_TABLE", tableName);
                rfc.SetValue("DELIMITER", "|");
                IRfcTable opt = rfc.GetTable("OPTIONS");
                IRfcStructure oRow = opt.Metadata.LineType.CreateStructure();
                oRow.SetValue("TEXT", where);
                opt.Append(oRow);

                IRfcTable flds = rfc.GetTable("FIELDS");
                foreach (var f in fields)
                {
                    IRfcStructure fRow = flds.Metadata.LineType.CreateStructure();
                    fRow.SetValue("FIELDNAME", f);
                    flds.Append(fRow);
                }

                rfc.Invoke(destination);
                IRfcTable data = rfc.GetTable("DATA");
                IRfcTable outFields = rfc.GetTable("FIELDS");
                for (int i = 0; i < data.RowCount; i++)
                {
                    string raw = data[i].GetString("WA");
                    string[] parts = raw.Split('|');
                    var dict = new Dictionary<string, string>();
                    dict["_RAW"] = raw;
                    for (int fi = 0; fi < outFields.RowCount && fi < parts.Length; fi++)
                    {
                        string fname = outFields[fi].GetString("FIELDNAME");
                        dict[fname] = parts[fi].Trim();
                    }
                    list.Add(dict);
                }
            }
            catch (Exception ex)
            {
                list.Add(new Dictionary<string, string> { { "ERROR", ex.Message } });
            }
            return list;
        };

        res["LIKP"] = queryTable("LIKP", new[] { "VBELN", "ERDAT", "LFDAT", "WADAT", "WADAT_IST", "LFART", "VSTEL", "KUNNR", "VKORG" }, $"VBELN = '{vbelnFmt}'");
        var likpList = res["LIKP"] as List<Dictionary<string, string>>;
        string kunnr = (likpList != null && likpList.Count > 0) ? likpList[0].GetValueOrDefault("KUNNR", "") : "";
        if (!string.IsNullOrEmpty(kunnr))
        {
            res["KNA1"] = queryTable("KNA1", new[] { "KUNNR", "NAME1", "NAME2", "STRAS", "ORT01" }, $"KUNNR = '{kunnr}'");
        }
        res["VBUK_WBSTK"] = queryTable("VBUK", new[] { "VBELN", "WBSTK" }, $"VBELN = '{vbelnFmt}'");
        res["DD03L_LIPS_VTWEG"] = queryTable("DD03L", new[] { "TABNAME", "FIELDNAME" }, "TABNAME = 'LIPS' AND FIELDNAME = 'VTWEG'");
        res["DD03L_LIKP_VTWEG"] = queryTable("DD03L", new[] { "TABNAME", "FIELDNAME" }, "TABNAME = 'LIKP' AND FIELDNAME = 'VTWEG'");
        res["LIPS_TEST_CON_VTWEG"] = queryTable("LIPS", new[] { "POSNR", "MATNR", "LFIMG" }, $"VBELN = '{vbelnFmt}' AND VTWEG = '65'");
        var lips = queryTable("LIPS", new[] { "POSNR", "MATNR", "ARKTX", "LFIMG", "LGMNG", "VRKME", "WERKS", "LGORT", "CHARG", "VGBEL", "VGPOS" }, $"VBELN = '{vbelnFmt}'");
        res["LIPS"] = lips;
        if (lips.Count > 0 && lips[0].ContainsKey("VGBEL"))
        {
            string vgbel = lips[0]["VGBEL"];
            string vgpos = lips[0].GetValueOrDefault("VGPOS", "000010");
            res["VBAP"] = queryTable("VBAP", new[] { "VBELN", "POSNR", "MATNR", "KWMENG", "VRKME", "MEINS" }, $"VBELN = '{vgbel}' AND POSNR = '{vgpos}'");
        }
        res["VBFA"] = queryTable("VBFA", new[] { "VBELV", "POSNV", "VBELN", "POSNN", "VBTYP_N" }, $"VBELV = '{vbelnFmt}'");

        if (lips.Count > 0 && lips[0].ContainsKey("MATNR"))
        {
            string matnr = lips[0]["MATNR"].PadLeft(18, '0');
            string werks = lips[0].GetValueOrDefault("WERKS", "6000");
            res["MCHB"] = queryTable("MCHB", new[] { "MATNR", "WERKS", "LGORT", "CHARG", "CLABS" }, $"MATNR = '{matnr}' AND WERKS = '{werks}'");
        }

        try
        {
            string matnr = lips.Count > 0 ? lips[0]["MATNR"].PadLeft(18, '0') : "";
            string werks = lips.Count > 0 ? lips[0].GetValueOrDefault("WERKS", "6000") : "6000";
            string lgort = lips.Count > 0 ? lips[0].GetValueOrDefault("LGORT", "6101") : "6101";
            decimal.TryParse(lips.Count > 0 ? lips[0].GetValueOrDefault("LFIMG", "1") : "1", NumberStyles.Any, CultureInfo.InvariantCulture, out decimal cant);

            // Obtener lote disponible de MCHB
            var lotesList = res.ContainsKey("MCHB") ? (List<Dictionary<string, string>>)res["MCHB"] : new List<Dictionary<string, string>>();
            string loteConStock = "";
            foreach (var l in lotesList)
            {
                decimal.TryParse(l.GetValueOrDefault("CLABS", "0"), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal st);
                if (st > 0)
                {
                    loteConStock = l.GetValueOrDefault("CHARG", "");
                    break;
                }
            }

            // Paso 1: Solo Picking y asignación de lote SIN WABUC
            IRfcFunction step1 = repository.CreateFunction("WS_DELIVERY_UPDATE");
            step1.SetValue("DELIVERY", vbelnFmt);
            step1.SetValue("UPDATE_PICKING", "X");
            step1.SetValue("SYNCHRON", "X");
            IRfcStructure tkok = step1.GetStructure("VBKOK_WA");
            tkok.SetValue("VBELN_VL", vbelnFmt);
            tkok.SetValue("VBELN", vbelnFmt);
            tkok.SetValue("KOMUE", "X");

            IRfcTable tpok = step1.GetTable("VBPOK_TAB");
            IRfcStructure trow = tpok.Metadata.LineType.CreateStructure();
            trow.SetValue("VBELN_VL", vbelnFmt);
            trow.SetValue("POSNR_VL", "000010");
            trow.SetValue("VBELN", vbelnFmt);
            trow.SetValue("POSNN", "000010");
            trow.SetValue("MATNR", matnr);
            trow.SetValue("CHARG", loteConStock);
            trow.SetValue("WERKS", werks);
            trow.SetValue("LGORT", lgort);
            trow.SetValue("PIKMG", cant);
            try { trow.SetValue("LIANP", "X"); } catch { }
            try { trow.SetValue("KZKFG", "X"); } catch { }
            tpok.Append(trow);

            step1.Invoke(destination);
            IRfcTable tprot = step1.GetTable("PROT");
            var msgs = new List<string>();
            for (int p = 0; p < tprot.RowCount; p++)
            {
                msgs.Add($"{tprot[p].GetString("MSGTY")} {tprot[p].GetString("MSGID")} {tprot[p].GetString("MSGNO")}: {tprot[p].GetString("MSGV1")} {tprot[p].GetString("MSGV2")}");
            }
            res["PASO1_PICKING_PROT"] = msgs;
            res["PASO1_LOTE_USADO"] = loteConStock;
            res["PASO1_CANT_USADA"] = cant;

            try
            {
                IRfcFunction rollback = repository.CreateFunction("BAPI_TRANSACTION_ROLLBACK");
                rollback.Invoke(destination);
            }
            catch { }
        }
        catch (Exception exTest)
        {
            res["PASO1_ERROR"] = exTest.Message;
        }

        return await Task.FromResult<object>(res);
    }
}

