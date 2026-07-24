using OperadorLogistico.Application.DTOs.Shared;
using OperadorLogistico.Application.DTOs.Recepcion;

namespace OperadorLogistico.Application.Interfaces;

public interface ISapRecepcionService
{
    Task<SapMovimientoResponseDto> ProcesarRecepcionAsync(RecepcionPedidoRequestDto request);
}
