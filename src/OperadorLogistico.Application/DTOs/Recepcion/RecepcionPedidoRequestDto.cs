namespace OperadorLogistico.Application.DTOs.Recepcion;

public class RecepcionItemDto
{
    public string Material { get; set; } = string.Empty;
    public string Centro { get; set; } = string.Empty;
    public string Almacen { get; set; } = string.Empty;
    public string ClaseMovimiento { get; set; } = "101"; // 101, 102, 122, 123
    public decimal Cantidad { get; set; }
    public string UnidadMedida { get; set; } = "UN";
    public string? Lote { get; set; }
    public string? TextoPosicion { get; set; }
    public int Posicion { get; set; } // Representa el PO_ITEM (ej: 10, 20) de la Orden de Compra
}

public class RecepcionPedidoRequestDto
{
    public string NumeroPedidoCompra { get; set; } = string.Empty;
    public string CodigoTransaccion { get; set; } = "01"; // GM_CODE 01
    public string TextoCabecera { get; set; } = "Recepción de Pedido de Compra";
    public bool EsSimulacion { get; set; } = false;
    public DateTime? FechaDocumento { get; set; }
    public DateTime? FechaContabilizacion { get; set; }
    public List<RecepcionItemDto> Items { get; set; } = new();
}
