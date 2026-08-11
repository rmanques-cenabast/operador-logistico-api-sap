namespace OperadorLogistico.Application.DTOs.Inventario;

public class ConsultaStockRequestDto
{
    public string Material { get; set; } = string.Empty;
    public string Centro { get; set; } = string.Empty;
    public string Almacen { get; set; } = string.Empty;
    public string Lote { get; set; } = string.Empty;
}
