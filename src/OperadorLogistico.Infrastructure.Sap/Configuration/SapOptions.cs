namespace OperadorLogistico.Infrastructure.Sap.Configuration;

public class SapOptions
{
    public const string SectionName = "SapSettings";

    public string AppServerHost { get; set; } = string.Empty;
    public string SystemNumber { get; set; } = "00";
    public string SystemID { get; set; } = string.Empty;
    public string Client { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Language { get; set; } = "ES";
    public string PoolSize { get; set; } = "5";
    public string PeakConnectionsLimit { get; set; } = "10";
}
