using OperadorLogistico.Application.DTOs.Documentos;

namespace OperadorLogistico.Application.Interfaces;

/// <summary>
/// Contrato para la gestión de anexos binarios vinculados a Objetos de Negocio en SAP (GOS).
/// </summary>
public interface ISapAnexoService
{
    /// <summary>
    /// Adjunta un archivo binario directamente a un Documento de Material (MIGO) en SAP.
    /// </summary>
    /// <param name="request">Datos del documento de material y contenido binario en Base64.</param>
    /// <returns>Resultado de la vinculación con el ID asignado por SAP.</returns>
    Task<AdjuntarDocumentoResponseDto> AdjuntarADocumentoMaterialAsync(AdjuntarDocumentoMaterialDto request);
}
