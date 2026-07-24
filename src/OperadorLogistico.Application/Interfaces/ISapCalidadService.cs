using OperadorLogistico.Application.DTOs.Shared;
using OperadorLogistico.Application.DTOs.Calidad;

namespace OperadorLogistico.Application.Interfaces;

public interface ISapCalidadService
{
    Task<SapMovimientoResponseDto> ProcesarTraspasoCalidadAsync(TraspasoCalidadRequestDto request);
    Task<SapMovimientoResponseDto> ProcesarMuestreoCalidadAsync(MuestreoCalidadRequestDto request);
}
