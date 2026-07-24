using OperadorLogistico.Application.DTOs.Shared;
using OperadorLogistico.Application.DTOs.AjustesBajas;

namespace OperadorLogistico.Application.Interfaces;

public interface ISapAjusteBajaService
{
    Task<SapMovimientoResponseDto> ProcesarAjusteBajaAsync(AjusteBajaRequestDto request);
}
