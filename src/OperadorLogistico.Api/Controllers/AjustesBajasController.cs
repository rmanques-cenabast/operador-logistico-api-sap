using Microsoft.AspNetCore.Mvc;
using OperadorLogistico.Application.DTOs.AjustesBajas;
using OperadorLogistico.Application.DTOs.Shared;
using OperadorLogistico.Application.Interfaces;

namespace OperadorLogistico.Api.Controllers;

[ApiController]
[Route("api/sap/ajustes-bajas")]
[EndpointGroupName("Ajuste Inventario - Ajustes y Bajas")]
public class AjustesBajasController : ControllerBase
{
    private readonly ISapAjusteBajaService _ajusteBajaService;

    public AjustesBajasController(ISapAjusteBajaService ajusteBajaService)
    {
        _ajusteBajaService = ajusteBajaService;
    }

    /// <summary>
    /// Procesa ajustes físicos por diferencia de inventario o desguace de material dañado (555, 711, 717) en SAP.
    /// </summary>
    [HttpPost("procesar")]
    [ProducesResponseType(typeof(SapMovimientoResponseDto), 200)]
    public async Task<IActionResult> ProcesarAjusteBaja([FromBody] AjusteBajaRequestDto request)
    {
        var result = await _ajusteBajaService.ProcesarAjusteBajaAsync(request);
        return Ok(result);
    }
}
