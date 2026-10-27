namespace SapSync.Core.Interfaces;

/// <summary>
/// Owns the process-wide SAP destination configuration. Must be registered as a singleton because
/// SAP NCo allows a destination configuration to be registered only once per process.
/// </summary>
public interface ISapDestinationManager
{
    string DestinationName { get; }

    /// <summary>Registers the destination configuration with SAP NCo exactly once (thread-safe, idempotent).</summary>
    void EnsureRegistered();
}
