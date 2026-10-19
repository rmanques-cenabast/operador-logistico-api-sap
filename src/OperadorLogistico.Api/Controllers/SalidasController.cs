using Microsoft.AspNetCore.Mvc;
using OperadorLogistico.Application.DTOs.Salida;
using OperadorLogistico.Application.DTOs.Shared;
using OperadorLogistico.Application.Interfaces;

namespace OperadorLogistico.Api.Controllers;

[ApiController]
[Route("api/sap/salidas")]
[EndpointGroupName("Salidas - Despachos y Picking")]
public class SalidasController : ControllerBase
{
    private readonly ISapSalidaService _salidaService;

    public SalidasController(ISapSalidaService salidaService)
    {
        _salidaService = salidaService;
    }

    /// <summary>
    /// Consulta y descarga las entregas de salida (VL01N) generadas en SAP pendientes de pickeo/despacho.
    /// Parámetro por defecto: últimos 3 meses hacia atrás.
    /// </summary>
    [HttpGet("entregas-pendientes")]
    [ProducesResponseType(typeof(ConsultaEntregasResponseDto), 200)]
    public async Task<IActionResult> ConsultarEntregasPendientes(
        [FromQuery] DateTime? fechaDesde, 
        [FromQuery] DateTime? fechaHasta, 
        [FromQuery] string? centro, 
        [FromQuery] string? almacen,
        [FromQuery] bool? soloPendientes)
    {
        var request = new ConsultaEntregasRequestDto
        {
            FechaDesde = fechaDesde ?? new DateTime(2026, 1, 1),
            FechaHasta = fechaHasta ?? new DateTime(2026, 7, 31),
            Centro = string.IsNullOrWhiteSpace(centro) ? "6000" : centro,
            Almacen = almacen,
            SoloPendientes = soloPendientes ?? true
        };

        var result = await _salidaService.ConsultarEntregasSalidaAsync(request);
        return Ok(result);
    }

    /// <summary>
    /// Contabiliza la Salida de Mercancías (Movimiento 601 / PGI) en SAP tras el despacho físico y emisión del DTE 52.
    /// </summary>
    [HttpPost("contabilizar-salida")]
    [ProducesResponseType(typeof(SapMovimientoResponseDto), 200)]
    [ProducesResponseType(typeof(SapMovimientoResponseDto), 400)]
    public async Task<IActionResult> ContabilizarSalida([FromBody] ConfirmacionDespachoRequestDto request)
    {
        var result = await _salidaService.ContabilizarSalidaMercanciasAsync(request);
        if (!result.Exitoso)
        {
            return BadRequest(result);
        }
        return Ok(result);
    }
}
