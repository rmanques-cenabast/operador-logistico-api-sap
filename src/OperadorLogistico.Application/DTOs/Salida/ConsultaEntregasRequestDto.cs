namespace OperadorLogistico.Application.DTOs.Salida;

public class ConsultaEntregasRequestDto
{
    /// <summary>
    /// Fecha de entrega inicial (LIKP-LFDAT), inclusive. Por defecto 01-01-2026.
    /// </summary>
    public DateTime? FechaDesde { get; set; } = new DateTime(2026, 1, 1);

    /// <summary>
    /// Fecha de entrega final (LIKP-LFDAT), inclusive. Por defecto 31-07-2026.
    /// </summary>
    public DateTime? FechaHasta { get; set; } = new DateTime(2026, 7, 31);

    /// <summary>
    /// Centro de Cenabast (ej. 6000).
    /// </summary>
    public string Centro { get; set; } = "6000";

    /// <summary>
    /// Almacén opcional.
    /// </summary>
    public string? Almacen { get; set; }

    /// <summary>
    /// Si es true, solo trae entregas con estado de salida de mercancias SM = A (VBUK-WBSTK), tanto FP como INT.
    /// </summary>
    public int Desplazamiento { get; set; } = 0;

    public bool SoloPendientes { get; set; } = true;

    /// <summary>
    /// Línea de negocio / Canal: 'INT' (Intermediación - Centros/Org 1000/3000) o 'FP' (Farmacias Privadas - Centro/Org 5000).
    /// </summary>
    public string? Canal { get; set; }

    /// <summary>
    /// Organización de Ventas específica (5000 para FP, 1000 o 3000 para INT).
    /// </summary>
    public string? OrganizacionVenta { get; set; }
}
