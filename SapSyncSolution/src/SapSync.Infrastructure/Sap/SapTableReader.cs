using System.Data;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SAP.Middleware.Connector;
using SapSync.Core.Interfaces;
using SapSync.Core.Models;

namespace SapSync.Infrastructure.Sap;

/// <summary>Reads SAP tables through <c>RFC_READ_TABLE</c> using the pooled destination.</summary>
public sealed partial class SapTableReader : ISapTableReader
{
    public const string FunctionName = "RFC_READ_TABLE";

    /// <summary>
    /// ASCII Unit Separator: cannot appear in normal business text, unlike '|', ',' or TAB,
    /// so splitting the DATA work area is unambiguous.
    /// </summary>
    public const char FieldDelimiter = '\u001F';

    private readonly IRfcDestinationProvider _destinationProvider;
    private readonly SapSettings _settings;
    private readonly ILogger<SapTableReader> _logger;

    public SapTableReader(IRfcDestinationProvider destinationProvider, IOptions<SapSettings> settings, ILogger<SapTableReader> logger)
    {
        _destinationProvider = destinationProvider ?? throw new ArgumentNullException(nameof(destinationProvider));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<DataTable> ReadTableAsync(SapQueryRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var tableName = NormalizeSapName(request.TableName, nameof(request.TableName));
        var requestedFields = (request.Fields ?? new List<string>())
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => NormalizeSapName(f, nameof(request.Fields)))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var optionLines = RfcOptionsBuilder.Split(request.FilterCondition);

        var destination = _destinationProvider.GetDestination();
        IRfcFunction function = destination.Repository.CreateFunction(FunctionName);

        function.SetValue("QUERY_TABLE", tableName);
        function.SetValue("DELIMITER", FieldDelimiter.ToString());
        if (request.RowSkip > 0)
        {
            function.SetValue("ROWSKIPS", request.RowSkip);
        }

        if (request.RowCount > 0)
        {
            function.SetValue("ROWCOUNT", request.RowCount);
        }

        IRfcTable options = function.GetTable("OPTIONS");
        foreach (var line in optionLines)
        {
            options.Append();
            options.SetValue("TEXT", line);
        }

        IRfcTable fields = function.GetTable("FIELDS");
        foreach (var field in requestedFields)
        {
            fields.Append();
            fields.SetValue("FIELDNAME", field);
        }

        _logger.LogDebug(
            "Invoking {FunctionName} for {SapTable} with {FieldCount} requested fields and {OptionLineCount} OPTIONS lines",
            FunctionName, tableName, requestedFields.Count, optionLines.Count);

        function.Invoke(destination);
        cancellationToken.ThrowIfCancellationRequested();

        var metadata = function.GetTable("FIELDS")
            .Select(f => new SapFieldMetadata(
                f.GetString("FIELDNAME").Trim(),
                f.GetString("TYPE").Trim(),
                f.GetInt("LENGTH"),
                f.GetString("FIELDTEXT")))
            .ToList();

        var rows = function.GetTable("DATA").Select(r => r.GetString("WA"));

        var table = SapDataTableBuilder.Build(tableName, metadata, rows, FieldDelimiter, _settings.ConvertAbapTypes);
        return Task.FromResult(table);
    }

    private static string NormalizeSapName(string value, string parameterName)
    {
        var name = value?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!SapNameRegex().IsMatch(name))
        {
            throw new ArgumentException($"'{value}' is not a valid SAP table or field name.", parameterName);
        }

        return name;
    }

    [GeneratedRegex("^[A-Z0-9_/]{1,30}$", RegexOptions.CultureInvariant)]
    private static partial Regex SapNameRegex();
}
