namespace OperadorLogistico.Application.DTOs.Traspasos;

public class TraspasoItemDto
{
    public string Material { get; set; } = string.Empty;
    public decimal Cantidad { get; set; }
    public string UnidadMedida { get; set; } = "UN";
    public string ClaseMovimiento { get; set; } = "311"; // 301, 309, 311, 411, 647
    
    // Nomenclatura semántica estructurada "Desde / Hacia"
    public string DesdeCentro { get; set; } = string.Empty;
    public string DesdeAlmacen { get; set; } = string.Empty;
    public string? DesdeLote { get; set; }
    
    public string? HaciaCentro { get; set; }
    public string? HaciaAlmacen { get; set; }
    public string? HaciaLote { get; set; }

    public string? MaterialDestino { get; set; }
    
    // Campo para definir el estado de stock destino (para traspasos a bloqueado o calidad)
    // Acepta valores como "S" (Bloqueado), "X" (Control Calidad) o "" (Libre Utilización)
    public string? TipoStockDestino { get; set; }
}

public class TraspasoRequestDto
{
    public string TextoCabecera { get; set; } = "Traspaso de Inventario OL";
    public bool EsSimulacion { get; set; } = false;
    public List<TraspasoItemDto> Items { get; set; } = new();
}
