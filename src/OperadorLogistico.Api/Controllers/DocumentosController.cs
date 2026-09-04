using Microsoft.AspNetCore.Mvc;
using OperadorLogistico.Application.DTOs.Documentos;
using OperadorLogistico.Application.Interfaces;

namespace OperadorLogistico.Api.Controllers;

[ApiController]
[Route("api/sap/documentos")]
[EndpointGroupName("Gestión Documental - Anexos SAP")]
public class DocumentosController : ControllerBase
{
    private readonly ISapAnexoService _anexoService;

    public DocumentosController(ISapAnexoService anexoService)
    {
        _anexoService = anexoService;
    }

    /// <summary>
    /// Adjunta un archivo binario (ej. Factura PDF) a un Documento de Material en SAP vía Generic Object Services (GOS).
    /// </summary>
    /// <param name="request">Datos del documento de material, nombre del archivo y contenido Base64.</param>
    /// <returns>Resultado de la vinculación en SAP.</returns>
    [HttpPost("adjuntar-migo")]
    [ProducesResponseType(typeof(AdjuntarDocumentoResponseDto), 200)]
    [ProducesResponseType(typeof(AdjuntarDocumentoResponseDto), 400)]
    public async Task<IActionResult> AdjuntarADocumentoMaterial([FromBody] AdjuntarDocumentoMaterialDto request)
    {
        var result = await _anexoService.AdjuntarADocumentoMaterialAsync(request);

        if (!result.Exitoso)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }
}
