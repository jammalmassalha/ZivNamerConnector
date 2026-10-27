using SAP.Middleware.Connector;
using SapSync.Core.Interfaces;

namespace SapSync.Infrastructure.Sap;

/// <summary>Infrastructure-level extension of <see cref="ISapDestinationManager"/> exposing the NCo destination.</summary>
public interface IRfcDestinationProvider : ISapDestinationManager
{
    /// <summary>Returns the pooled NCo destination, registering the configuration on first use.</summary>
    RfcDestination GetDestination();
}
