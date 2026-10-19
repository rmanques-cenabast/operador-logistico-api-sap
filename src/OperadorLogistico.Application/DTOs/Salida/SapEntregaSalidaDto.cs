namespace OperadorLogistico.Application.DTOs.Salida;

public class SapEntregaSalidaItemDto
{
    public int Posicion { get; set; }
    public string CodigoMaterial { get; set; } = string.Empty;
    public string DescripcionMaterial { get; set; } = string.Empty;
    public decimal CantidadSolicitada { get; set; }
    public decimal CantidadPickeada { get; set; } = 0;
    public string UnidadMedida { get; set; } = "UN";
    public string Centro { get; set; } = "6000";
    public string Almacen { get; set; } = "6001";
    public string? Lote { get; set; }
    public string? UbicacionSugerida { get; set; }
    public string EstadoItem { get; set; } = "PENDIENTE";
}

public class SapEntregaSalidaDto
{
    public string NumeroEntrega { get; set; } = string.Empty;
    public string PedidoVenta { get; set; } = string.Empty;
    public string Destinatario { get; set; } = string.Empty;
    public string DireccionDestino { get; set; } = string.Empty;
    public string Comuna { get; set; } = string.Empty;
    public string Ciudad { get; set; } = "SANTIAGO";
    public string Prioridad { get; set; } = "NORMAL";
    public DateTime FechaCreacion { get; set; }
    public DateTime FechaCompromiso { get; set; }
    public string Estado { get; set; } = "PENDIENTE_PICKING";
    public string? FolioDte { get; set; }
    public string EstadoSap { get; set; } = "PENDIENTE";
    public string? DocumentoSapPgi { get; set; }
    public List<SapEntregaSalidaItemDto> Lineas { get; set; } = new();
}
