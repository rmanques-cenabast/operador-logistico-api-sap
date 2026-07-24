using SAP.Middleware.Connector;

namespace OperadorLogistico.Infrastructure.Sap.Configuration;

public class SapDestinationConfiguration : IDestinationConfiguration
{
    private readonly SapOptions _options;

    public SapDestinationConfiguration(SapOptions options)
    {
        _options = options;
    }

    public event RfcDestinationManager.ConfigurationChangeHandler ConfigurationChanged;

    public RfcConfigParameters GetParameters(string destinationName)
    {
        if (destinationName.Equals("QAS_DEST", StringComparison.OrdinalIgnoreCase) ||
            destinationName.Equals(_options.SystemID, StringComparison.OrdinalIgnoreCase))
        {
            var parms = new RfcConfigParameters();
            parms.Add(RfcConfigParameters.AppServerHost, _options.AppServerHost);
            parms.Add(RfcConfigParameters.SystemNumber, _options.SystemNumber);
            parms.Add(RfcConfigParameters.SystemID, _options.SystemID);
            parms.Add(RfcConfigParameters.Client, _options.Client);
            parms.Add(RfcConfigParameters.User, _options.User);
            parms.Add(RfcConfigParameters.Password, _options.Password);
            parms.Add(RfcConfigParameters.Language, _options.Language);
            parms.Add(RfcConfigParameters.PoolSize, _options.PoolSize);
            parms.Add(RfcConfigParameters.PeakConnectionsLimit, _options.PeakConnectionsLimit);

            return parms;
        }

        return null!;
    }

    public bool ChangeEventsSupported() => false;
}
