namespace OperadorLogistico.Application.DTOs.AjustesBajas;

public class AjusteBajaItemDto
{
    public string Material { get; set; } = string.Empty;
    public string Centro { get; set; } = string.Empty;
    public string Almacen { get; set; } = string.Empty;
    public string ClaseMovimiento { get; set; } = "555"; // 555, 711, 712, 717, 718
    public decimal Cantidad { get; set; }
    public string UnidadMedida { get; set; } = "UN";
    public string? Lote { get; set; }
    public string? CentroCosto { get; set; } // Opcional, a veces requerido para desguaces (555)
}

public class AjusteBajaRequestDto
{
    public DateTime? FechaDocumento { get; set; }
    public DateTime? FechaContabilizacion { get; set; }
    public string TextoCabecera { get; set; } = "Ajuste o Merma Inventario OL";
    public bool EsSimulacion { get; set; } = false;
    public List<AjusteBajaItemDto> Items { get; set; } = new();
}
