using OperadorLogistico.Application.DTOs.Inventario;

namespace OperadorLogistico.Application.Interfaces;

public interface ISapStockService
{
    Task<ConsultaStockResponseDto> ConsultarStockLoteAsync(ConsultaStockRequestDto request);
}
