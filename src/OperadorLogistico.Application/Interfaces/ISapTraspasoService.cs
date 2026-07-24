using OperadorLogistico.Application.DTOs.Shared;
using OperadorLogistico.Application.DTOs.Traspasos;

namespace OperadorLogistico.Application.Interfaces;

public interface ISapTraspasoService
{
    Task<SapMovimientoResponseDto> ProcesarTraspasoAsync(TraspasoRequestDto request);
}
