using SAP.Middleware.Connector;

namespace OperadorLogistico.Infrastructure.Sap.Services;

public interface ISapConnectionFactory
{
    RfcDestination GetDestination();
}

public class SapConnectionFactory : ISapConnectionFactory
{
    private const string DestinationName = "QAS_DEST";

    public RfcDestination GetDestination()
    {
        return RfcDestinationManager.GetDestination(DestinationName);
    }
}
