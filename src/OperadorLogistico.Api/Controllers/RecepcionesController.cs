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
    public async Task<IActionResult> ProcesarRecepcion([FromBody] RecepcionPedidoRequestDto request)
    {
        var result = await _recepcionService.ProcesarRecepcionAsync(request);
        return Ok(result);
    }
}
