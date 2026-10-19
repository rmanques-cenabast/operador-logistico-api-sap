namespace OperadorLogistico.Application.DTOs.Salida;

public class ConsultaEntregasRequestDto
{
    /// <summary>
    /// Fecha de creación inicial. Por defecto Enero 2026 (o rango histórico SAP DEV).
    /// </summary>
    public DateTime? FechaDesde { get; set; } = new DateTime(2026, 1, 1);

    /// <summary>
    /// Fecha de creación final. Por defecto Julio 2026 (31 de Julio) ya que la data de SAP DEV/QAS está hasta Julio.
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
    /// Si es true, solo trae entregas cuya salida de mercancías (PGI) aún no ha sido contabilizada.
    /// </summary>
    public bool SoloPendientes { get; set; } = true;
}
