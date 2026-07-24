namespace OperadorLogistico.Domain.Interfaces;

public interface ISapStatusService
{
    Task<string> GetSapStatusAsync();
    Task<object> VerificarBapiAsync(string bapiName);
}
