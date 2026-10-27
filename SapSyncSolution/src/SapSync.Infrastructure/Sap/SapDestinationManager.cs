using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SAP.Middleware.Connector;
using SapSync.Core.Models;

namespace SapSync.Infrastructure.Sap;

/// <summary>
/// Thread-safe singleton that supplies SAP NCo with destination (logon + connection pool) parameters.
/// <see cref="RfcDestinationManager.RegisterDestinationConfiguration"/> may only be called once per process,
/// so registration is guarded with double-checked locking on process-wide state.
/// </summary>
public sealed class SapDestinationManager : IRfcDestinationProvider, IDestinationConfiguration
{
    private static readonly object RegistrationLock = new();
    private static volatile bool _isRegistered;

    private readonly SapSettings _settings;
    private readonly ILogger<SapDestinationManager> _logger;

    public SapDestinationManager(IOptions<SapSettings> settings, ILogger<SapDestinationManager> logger)
    {
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string DestinationName => _settings.DestinationName;

    public void EnsureRegistered()
    {
        if (_isRegistered)
        {
            return;
        }

        lock (RegistrationLock)
        {
            if (_isRegistered)
            {
                return;
            }

            RfcDestinationManager.RegisterDestinationConfiguration(this);
            _isRegistered = true;

            _logger.LogInformation(
                "Registered SAP destination {DestinationName} (PoolSize={PoolSize}, PeakConnectionsLimit={PeakConnectionsLimit}, ConnectionIdleTimeout={ConnectionIdleTimeout}s)",
                _settings.DestinationName, _settings.PoolSize, _settings.PeakConnectionsLimit, _settings.ConnectionIdleTimeout);
        }
    }

    public RfcDestination GetDestination()
    {
        EnsureRegistered();
        return RfcDestinationManager.GetDestination(_settings.DestinationName);
    }

    /// <summary>Called by NCo; returns <c>null</c> for destinations this provider does not own.</summary>
    public RfcConfigParameters? GetParameters(string destinationName)
    {
        if (!string.Equals(destinationName, _settings.DestinationName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var parameters = new RfcConfigParameters
        {
            [RfcConfigParameters.Name] = _settings.DestinationName,
            [RfcConfigParameters.SystemNumber] = _settings.SystemNumber,
            [RfcConfigParameters.Client] = _settings.Client,
            [RfcConfigParameters.User] = _settings.User.Trim(), // " bapiapp" would fail the SAP logon
            [RfcConfigParameters.Password] = _settings.Password,
            [RfcConfigParameters.Language] = _settings.Language,
            [RfcConfigParameters.PoolSize] = _settings.PoolSize.ToString(CultureInfo.InvariantCulture),
            [RfcConfigParameters.PeakConnectionsLimit] = _settings.PeakConnectionsLimit.ToString(CultureInfo.InvariantCulture),
            [RfcConfigParameters.ConnectionIdleTimeout] = _settings.ConnectionIdleTimeout.ToString(CultureInfo.InvariantCulture),
        };

        if (!string.IsNullOrWhiteSpace(_settings.AppServerHost))
        {
            parameters[RfcConfigParameters.AppServerHost] = _settings.AppServerHost.Trim();
        }
        else
        {
            parameters[RfcConfigParameters.MessageServerHost] = _settings.MessageServerHost!;
            parameters[RfcConfigParameters.LogonGroup] = _settings.LogonGroup!;
        }

        if (!string.IsNullOrWhiteSpace(_settings.SystemId))
        {
            parameters[RfcConfigParameters.SystemID] = _settings.SystemId;
        }

        if (!string.IsNullOrWhiteSpace(_settings.SapRouter))
        {
            parameters[RfcConfigParameters.SAPRouter] = _settings.SapRouter;
        }

        return parameters;
    }

    /// <summary>Configuration is static for the lifetime of the process.</summary>
    public bool ChangeEventsSupported() => false;

    public event RfcDestinationManager.ConfigurationChangeHandler? ConfigurationChanged
    {
        add { }
        remove { }
    }
}
