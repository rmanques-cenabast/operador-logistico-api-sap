using Microsoft.AspNetCore.Mvc;
using OperadorLogistico.Domain.Interfaces;

namespace OperadorLogistico.Api.Controllers;

[ApiController]
[Route("api/sap")]
[EndpointGroupName("Ajuste Inventario - Utilidades SAP")]
public class SapController : ControllerBase
{
    private readonly ISapStatusService _sapStatusService;

    public SapController(ISapStatusService sapStatusService)
    {
        _sapStatusService = sapStatusService;
    }

    /// <summary>
    /// Prueba la conexión activa con el servidor SAP enviando un Ping NCo.
    /// </summary>
    [HttpGet("probar-conexion")]
    public async Task<IActionResult> ProbarConexion()
    {
        var sapStatus = await _sapStatusService.GetSapStatusAsync();
        return Ok(new
        {
            Status = "Online",
            Timestamp = DateTime.UtcNow,
            SapStatus = sapStatus
        });
    }

    /// <summary>
    /// Verifica si una BAPI o RFC está habilitada y disponible en el catálogo de SAP.
    /// </summary>
    /// <param name="bapiName">Nombre de la BAPI (por defecto BAPI_GOODSMVT_CREATE)</param>
    [HttpGet("verificar-bapi")]
    public async Task<IActionResult> VerificarBapi([FromQuery] string bapiName = "BAPI_GOODSMVT_CREATE")
    {
        var resultado = await _sapStatusService.VerificarBapiAsync(bapiName);
        return Ok(new
        {
            Bapi = bapiName.ToUpper(),
            Detalle = resultado,
            Timestamp = DateTime.UtcNow
        });
    }
}
