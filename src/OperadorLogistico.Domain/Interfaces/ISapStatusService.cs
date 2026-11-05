namespace OperadorLogistico.Domain.Interfaces;

public interface ISapStatusService
{
    Task<string> GetSapStatusAsync();
    Task<object> VerificarBapiAsync(string bapiName);
    Task<List<string>> QueryTableAsync(string tableName, string fields, string where = "", int rowCount = 20);
}
