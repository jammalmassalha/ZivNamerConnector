using System.ComponentModel.DataAnnotations;

namespace SapSync.Core.Models;

/// <summary>
/// Strongly typed SAP NCo destination and connection pool configuration (section "SapSettings").
/// Either <see cref="AppServerHost"/> (direct application server logon) or
/// <see cref="MessageServerHost"/> + <see cref="LogonGroup"/> (load-balanced logon) must be supplied.
/// </summary>
public sealed class SapSettings : IValidatableObject
{
    public const string SectionName = "SapSettings";

    [Required]
    public string DestinationName { get; set; } = "SAP_ERP";

    public string? AppServerHost { get; set; }

    public string? MessageServerHost { get; set; }

    public string? LogonGroup { get; set; }

    [Required, RegularExpression("^[0-9]{2}$", ErrorMessage = "SystemNumber must be two digits, e.g. \"00\".")]
    public string SystemNumber { get; set; } = "00";

    public string? SystemId { get; set; }

    [Required, RegularExpression("^[0-9]{3}$", ErrorMessage = "Client must be three digits, e.g. \"100\".")]
    public string Client { get; set; } = "100";

    [Required]
    public string User { get; set; } = string.Empty;

    /// <summary>Supply via user-secrets / environment variables (SapSettings__Password), never in source control.</summary>
    public string Password { get; set; } = string.Empty;

    [Required]
    public string Language { get; set; } = "EN";

    public string? SapRouter { get; set; }

    /// <summary>Maximum number of idle connections kept open in the pool.</summary>
    [Range(1, 1000)]
    public int PoolSize { get; set; } = 5;

    /// <summary>Maximum number of simultaneously open connections for the destination.</summary>
    [Range(1, 1000)]
    public int PeakConnectionsLimit { get; set; } = 10;

    /// <summary>Seconds after which an idle pooled connection is closed.</summary>
    [Range(1, 86400)]
    public int ConnectionIdleTimeout { get; set; } = 600;

    /// <summary>
    /// When true (default) ABAP types are converted to CLR types (D → DateTime, T → TimeSpan, P → decimal, I → int, F → double).
    /// Set to false when the SQL target stores SAP values in character columns.
    /// </summary>
    public bool ConvertAbapTypes { get; set; } = true;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var hasAppServer = !string.IsNullOrWhiteSpace(AppServerHost);
        var hasMessageServer = !string.IsNullOrWhiteSpace(MessageServerHost);

        if (!hasAppServer && !hasMessageServer)
        {
            yield return new ValidationResult(
                "Either AppServerHost or MessageServerHost must be configured.",
                new[] { nameof(AppServerHost), nameof(MessageServerHost) });
        }

        if (hasMessageServer && (string.IsNullOrWhiteSpace(LogonGroup) || string.IsNullOrWhiteSpace(SystemId)))
        {
            yield return new ValidationResult(
                "LogonGroup and SystemId are required when MessageServerHost is used.",
                new[] { nameof(LogonGroup), nameof(SystemId) });
        }

        if (PoolSize > PeakConnectionsLimit)
        {
            yield return new ValidationResult(
                "PoolSize must not exceed PeakConnectionsLimit.",
                new[] { nameof(PoolSize), nameof(PeakConnectionsLimit) });
        }
    }
}
