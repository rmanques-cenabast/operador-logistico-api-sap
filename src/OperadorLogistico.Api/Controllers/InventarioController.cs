using Microsoft.AspNetCore.Mvc;
using OperadorLogistico.Application.DTOs.Inventario;
using OperadorLogistico.Application.Interfaces;

namespace OperadorLogistico.Api.Controllers;

[ApiController]
[Route("api/sap/[controller]")]
public class InventarioController : ControllerBase
{
    private readonly ISapStockService _stockService;

    public InventarioController(ISapStockService stockService)
    {
        _stockService = stockService;
    }

    [HttpGet("stock")]
    public async Task<IActionResult> GetStock([FromQuery] string material, [FromQuery] string centro, [FromQuery] string almacen, [FromQuery] string lote)
    {
        if (string.IsNullOrEmpty(material) || string.IsNullOrEmpty(centro) || string.IsNullOrEmpty(almacen))
        {
            return BadRequest(new { Mensaje = "Material, Centro y Almacén son requeridos." });
        }

        var req = new ConsultaStockRequestDto
        {
            Material = material,
            Centro = centro,
            Almacen = almacen,
            Lote = lote ?? string.Empty
        };

        var response = await _stockService.ConsultarStockLoteAsync(req);

        if (!response.Exitoso)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }
}
