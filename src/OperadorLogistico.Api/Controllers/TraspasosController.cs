using Microsoft.AspNetCore.Mvc;
using OperadorLogistico.Application.DTOs.Traspasos;
using OperadorLogistico.Application.DTOs.Shared;
using OperadorLogistico.Application.Interfaces;

namespace OperadorLogistico.Api.Controllers;

[ApiController]
[Route("api/sap/traspasos")]
[EndpointGroupName("Ajuste Inventario - Traspasos")]
public class TraspasosController : ControllerBase
{
    private readonly ISapTraspasoService _traspasoService;

    public TraspasosController(ISapTraspasoService traspasoService)
    {
        _traspasoService = traspasoService;
    }

    /// <summary>
    /// Procesa traspasos de stock internos en Libre Utilización (centro a centro, material a material, almacén a almacén, etc.).
    /// </summary>

    [HttpPost("interno")]
    [ProducesResponseType(typeof(SapMovimientoResponseDto), 200)]
    public async Task<IActionResult> ProcesarTraspaso([FromBody] TraspasoRequestDto request)
    {
        var result = await _traspasoService.ProcesarTraspasoAsync(request);
        return Ok(result);
    }
}
