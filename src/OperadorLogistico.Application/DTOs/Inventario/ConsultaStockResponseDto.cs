using OperadorLogistico.Application.DTOs.Shared;

namespace OperadorLogistico.Application.DTOs.Inventario;

public class ConsultaStockResponseDto
{
    public bool Exitoso { get; set; }
    public List<BapiReturnMessageDto> Mensajes { get; set; } = new();
    public decimal Libre { get; set; }
    public decimal Calidad { get; set; }
    public decimal Bloqueado { get; set; }
}
