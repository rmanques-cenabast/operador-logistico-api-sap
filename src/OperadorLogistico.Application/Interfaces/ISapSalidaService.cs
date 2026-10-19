using OperadorLogistico.Application.DTOs.Salida;
using OperadorLogistico.Application.DTOs.Shared;

namespace OperadorLogistico.Application.Interfaces;

public interface ISapSalidaService
{
    Task<ConsultaEntregasResponseDto> ConsultarEntregasSalidaAsync(ConsultaEntregasRequestDto request);
    Task<SapMovimientoResponseDto> ContabilizarSalidaMercanciasAsync(ConfirmacionDespachoRequestDto request);
}
