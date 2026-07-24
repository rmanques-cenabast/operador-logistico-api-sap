namespace OperadorLogistico.Application.DTOs.Calidad;

public class MuestreoCalidadItemDto
{
    public string Material { get; set; } = string.Empty;
    public string Centro { get; set; } = string.Empty;
    public string Almacen { get; set; } = string.Empty;
    public string ClaseMovimiento { get; set; } = "331"; // 331 o 332
    public decimal Cantidad { get; set; }
    public string UnidadMedida { get; set; } = "UN";
    public string? Lote { get; set; }
    public string? CentroCosto { get; set; } // Requerido para imputación de salida por muestreo (331)
}

public class MuestreoCalidadRequestDto
{
    public DateTime FechaDocumento { get; set; } = DateTime.Today;
    public DateTime FechaContabilizacion { get; set; } = DateTime.Today;
    public string TextoCabecera { get; set; } = "Muestreo de Calidad OL";
    public bool EsSimulacion { get; set; } = false;
    public List<MuestreoCalidadItemDto> Items { get; set; } = new();
}

public class TraspasoCalidadItemDto
{
    public string Material { get; set; } = string.Empty;
    public string Centro { get; set; } = string.Empty;
    public string AlmacenOrigen { get; set; } = string.Empty; // Calidad
    public string AlmacenDestino { get; set; } = string.Empty; // Libre o Bloqueado
    public string ClaseMovimiento { get; set; } = "321"; // 321, 322, 350, 349
    public decimal Cantidad { get; set; }
    public string UnidadMedida { get; set; } = "UN";
    public string? Lote { get; set; }
}

public class TraspasoCalidadRequestDto
{
    public DateTime FechaDocumento { get; set; } = DateTime.Today;
    public DateTime FechaContabilizacion { get; set; } = DateTime.Today;
    public string TextoCabecera { get; set; } = "Traspaso de Control de Calidad";
    public bool EsSimulacion { get; set; } = false;
    public List<TraspasoCalidadItemDto> Items { get; set; } = new();
}
