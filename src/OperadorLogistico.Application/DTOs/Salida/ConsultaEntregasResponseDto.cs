using OperadorLogistico.Application.DTOs.Shared;

namespace OperadorLogistico.Application.DTOs.Salida;

public class ConsultaEntregasResponseDto
{
    public bool Exitoso { get; set; } = true;
    public int SiguienteDesplazamiento { get; set; }
    public bool HayMas { get; set; }
    public int LeidasSap { get; set; }
    public int TamanoBloque { get; set; } = 100;
    public int TotalEntregas { get; set; }
    public DateTime FechaDesde { get; set; }
    public DateTime FechaHasta { get; set; }
    public List<SapEntregaSalidaDto> Entregas { get; set; } = new();
    public List<BapiReturnMessageDto> Mensajes { get; set; } = new();
}

public class ConfirmacionDespachoItemDto
{
    public int Posicion { get; set; }
    public string CodigoMaterial { get; set; } = string.Empty;
    public decimal CantidadDespachada { get; set; }
    public string LoteSap { get; set; } = string.Empty;
    public string UnidadMedida { get; set; } = "UN";
}

public class ConfirmacionDespachoRequestDto
{
    public string NumeroEntregaSap { get; set; } = string.Empty;
    public string FolioDte { get; set; } = string.Empty;
    public string UsuarioOl { get; set; } = "OPERADOR_BODEGA";
    public DateTime FechaDespacho { get; set; } = DateTime.Now;
    public List<ConfirmacionDespachoItemDto> Items { get; set; } = new();
}
