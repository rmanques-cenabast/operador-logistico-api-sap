using System.ComponentModel.DataAnnotations;

namespace OperadorLogistico.Application.DTOs.Documentos;

/// <summary>
/// Modelo de solicitud para adjuntar un archivo binario a un Documento de Material en SAP (GOS).
/// </summary>
public class AdjuntarDocumentoMaterialDto
{
    /// <summary>
    /// Folio del Documento de Material en SAP (ej: "5000498067").
    /// </summary>
    [Required(ErrorMessage = "El número de documento de material es obligatorio.")]
    public string NumeroDocumentoMaterial { get; set; } = string.Empty;

    /// <summary>
    /// Ejercicio fiscal / Año de contabilización del documento (ej: "2026").
    /// Si no se especifica, se utiliza el año actual.
    /// </summary>
    public string? Ejercicio { get; set; }

    /// <summary>
    /// Nombre del archivo con su extensión (ej: "Factura_12345.pdf").
    /// </summary>
    [Required(ErrorMessage = "El nombre del archivo es obligatorio.")]
    public string NombreArchivo { get; set; } = string.Empty;

    /// <summary>
    /// Contenido binario del archivo codificado en Base64.
    /// </summary>
    [Required(ErrorMessage = "El contenido del archivo en Base64 es obligatorio.")]
    public string ContenidoBase64 { get; set; } = string.Empty;

    /// <summary>
    /// Tipo o extensión de documento SAP (ej: "PDF", "EXT", "BIN").
    /// </summary>
    public string? TipoDocumento { get; set; }
}

/// <summary>
/// Modelo de respuesta tras adjuntar un documento en SAP.
/// </summary>
public class AdjuntarDocumentoResponseDto
{
    public bool Exitoso { get; set; }
    public string Mensaje { get; set; } = string.Empty;
    public string? DocumentoIdSAP { get; set; }
    public string? ObjetoClaveSAP { get; set; }
}
