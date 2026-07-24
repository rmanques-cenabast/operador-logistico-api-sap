namespace OperadorLogistico.Application.DTOs.Shared;

public class BapiReturnMessageDto
{
    public string Tipo { get; set; } = string.Empty; // S: Success, E: Error, W: Warning, I: Info, A: Abort
    public string Mensaje { get; set; } = string.Empty;
    public string CodigoMensaje { get; set; } = string.Empty; // Mantenido para retrocompatibilidad
    public string IdMensaje { get; set; } = string.Empty; // Clase de mensaje (MSGID)
    public string NumeroMensaje { get; set; } = string.Empty; // Número de mensaje (MSGNO)
    public string Variable1 { get; set; } = string.Empty;
    public string Variable2 { get; set; } = string.Empty;
    public string Variable3 { get; set; } = string.Empty;
    public string Variable4 { get; set; } = string.Empty;
    public string Parametro { get; set; } = string.Empty; // Nombre de parámetro con error
    public int Fila { get; set; } // Fila/Línea del ítem afectado
}

public class SapMovimientoResponseDto
{
    public bool Exitoso { get; set; }
    public bool EsSimulacion { get; set; }
    public string? DocumentoMaterial { get; set; }
    public string? Ejercicio { get; set; }
    public List<BapiReturnMessageDto> Mensajes { get; set; } = new();
}
