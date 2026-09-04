using Microsoft.AspNetCore.Mvc;
using OperadorLogistico.Application.DTOs.Recepcion;
using OperadorLogistico.Application.DTOs.Shared;
using OperadorLogistico.Application.Interfaces;

namespace OperadorLogistico.Api.Controllers;

[ApiController]
[Route("api/sap/recepciones")]
[EndpointGroupName("Ajuste Inventario - Recepciones")]
public class RecepcionesController : ControllerBase
{
    private readonly ISapRecepcionService _recepcionService;

    public RecepcionesController(ISapRecepcionService recepcionService)
    {
        _recepcionService = recepcionService;
    }

    /// <summary>
    /// Procesa la Entrada de Mercancías por Pedido de Compra o Consignación en SAP (BAPI_GOODSMVT_CREATE).
    /// </summary>
    [HttpPost("pedido-compra")]
    [ProducesResponseType(typeof(SapMovimientoResponseDto), 200)]
    [ProducesResponseType(typeof(SapMovimientoResponseDto), 400)]
    public async Task<IActionResult> ProcesarRecepcion([FromBody] RecepcionPedidoRequestDto request)
    {
        var result = await _recepcionService.ProcesarRecepcionAsync(request);
        
        if (!result.Exitoso)
        {
            return BadRequest(result); // Mejor práctica REST: 400 Bad Request si SAP lo rechaza
        }
        
        return Ok(result);
    }

    /// <summary>
    /// Consulta el estado de liberación en SAP de un Pedido de Compra (BAPI_PO_GETRELINFO / BAPI_PO_GETDETAIL).
    /// </summary>
    [HttpGet("pedido-compra/{numeroPedido}/liberacion")]
    [ProducesResponseType(typeof(PedidoCompraLiberacionDto), 200)]
    public async Task<IActionResult> ConsultarEstadoLiberacion([FromRoute] string numeroPedido)
    {
        var result = await _recepcionService.ConsultarEstadoLiberacionAsync(numeroPedido);
        return Ok(result);
    }
}
