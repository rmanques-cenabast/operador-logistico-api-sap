using Microsoft.AspNetCore.Mvc;
using OperadorLogistico.Application.DTOs.Calidad;
using OperadorLogistico.Application.DTOs.Shared;
using OperadorLogistico.Application.Interfaces;

namespace OperadorLogistico.Api.Controllers;

[ApiController]
[Route("api/sap/calidad")]
[EndpointGroupName("Ajuste Inventario - Control de Calidad")]
public class CalidadController : ControllerBase
{
    private readonly ISapCalidadService _calidadService;

    public CalidadController(ISapCalidadService calidadService)
    {
        _calidadService = calidadService;
    }

    /// <summary>
    /// Procesa el traspaso de stock de Control de Calidad hacia Libre Utilización o Bloqueado (BAPI_GOODSMVT_CREATE).
    /// </summary>
    [HttpPost("traspaso")]
    [ProducesResponseType(typeof(SapMovimientoResponseDto), 200)]
    public async Task<IActionResult> ProcesarTraspasoCalidad([FromBody] TraspasoCalidadRequestDto request)
    {
        var result = await _calidadService.ProcesarTraspasoCalidadAsync(request);
        return Ok(result);
    }

    /// <summary>
    /// Registra la salida de stock por muestreo de Control de Calidad hacia el laboratorio (BAPI_GOODSMVT_CREATE).
    /// </summary>
    [HttpPost("muestreo")]
    [ProducesResponseType(typeof(SapMovimientoResponseDto), 200)]
    public async Task<IActionResult> ProcesarMuestreoCalidad([FromBody] MuestreoCalidadRequestDto request)
    {
        var result = await _calidadService.ProcesarMuestreoCalidadAsync(request);
        return Ok(result);
    }
}
