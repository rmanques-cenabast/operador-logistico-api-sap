namespace OperadorLogistico.Application.DTOs.Recepcion;

public class PedidoCompraLiberacionDto
{
    public string NumeroPedidoCompra { get; set; } = string.Empty;
    public string RutProveedor { get; set; } = string.Empty;
    public string NombreProveedor { get; set; } = string.Empty;
    public string ClaseDocumento { get; set; } = string.Empty;
    public string FechaCreacion { get; set; } = string.Empty;
    
    // Estado de Liberación
    public bool EstaLiberado { get; set; }
    public string CodigoLiberacion { get; set; } = string.Empty;
    public string EstadoLiberacionTexto { get; set; } = string.Empty;
    public string GrupoLiberacion { get; set; } = string.Empty;
    public string EstrategiaLiberacion { get; set; } = string.Empty;
    
    // Niveles / Firmas (si aplica)
    public List<NivelLiberacionDto> NivelesLiberacion { get; set; } = new();
    
    // Mensajes o notas
    public string Mensaje { get; set; } = string.Empty;
    public bool Exitoso { get; set; } = true;
}

public class NivelLiberacionDto
{
    public string Codigo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public bool Aprobado { get; set; }
}
