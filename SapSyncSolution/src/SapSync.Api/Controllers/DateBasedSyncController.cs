using Microsoft.AspNetCore.Mvc;
using SapSync.Core.Interfaces;
using SapSync.Core.Models;

namespace SapSync.Api.Controllers;

/// <summary>
/// REST API endpoints for date-based incremental table syncs from SAP.
/// Filters tables by INERTDATE and UPDATEDATE columns using SAP date format (YYYYMMDD).
/// </summary>
[ApiController]
[Route("api/sync")]
[Produces("application/json")]
public sealed class DateBasedSyncController : ControllerBase
{
    private readonly ITableSyncService _tableSyncService;
    private readonly IDateBasedSyncService _dateBasedSyncService;
    private readonly ILogger<DateBasedSyncController> _logger;

    public DateBasedSyncController(
        ITableSyncService tableSyncService,
        IDateBasedSyncService dateBasedSyncService,
        ILogger<DateBasedSyncController> logger)
    {
        _tableSyncService = tableSyncService ?? throw new ArgumentNullException(nameof(tableSyncService));
        _dateBasedSyncService = dateBasedSyncService ?? throw new ArgumentNullException(nameof(dateBasedSyncService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Sync a table using date-based filtering (INERTDATE >= date-N and/or UPDATEDATE >= date-N).
    /// Records are fetched where INERTDATE or UPDATEDATE is greater than or equal to the specified days back.
    /// </summary>
    /// <param name="tableName">Name of the configured table to sync (e.g., "NPAT", "NFAL")</param>
    /// <param name="daysBack">Number of days back from today (default 1 = yesterday). Use 0 for today.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Table sync result with row counts and timing</returns>
    [HttpPost("table/{tableName}/by-date")]
    [ProducesResponseType(typeof(TableSyncResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponseDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponseDto), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<TableSyncResult>> SyncTableByDate(
        string tableName,
        [FromQuery] int daysBack = 1,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_tableSyncService.IsConfigured(tableName))
            {
                return BadRequest(new ErrorResponseDto
                {
                    Error = $"Table '{tableName}' is not configured",
                    Message = $"Available tables: {string.Join(", ", _tableSyncService.TableNames)}"
                });
            }

            _logger.LogInformation("Syncing table {TableName} with dual-date filter (daysBack={DaysBack})", tableName, daysBack);

            // Use LookbackDays to trigger DeltaSyncFilterBuilder which uses table's configured date fields
            var request = new TableSyncRequest { LookbackDays = daysBack };
            var result = await _tableSyncService.SyncTableAsync(tableName, request, cancellationToken);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing table {TableName}", tableName);
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponseDto
            {
                Error = "Sync operation failed",
                Message = ex.Message
            });
        }
    }

    /// <summary>
    /// Sync a table using only INERTDATE filtering (INERTDATE >= date-N).
    /// </summary>
    /// <param name="tableName">Name of the configured table to sync</param>
    /// <param name="daysBack">Number of days back from today (default 1 = yesterday)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Table sync result</returns>
    [HttpPost("table/{tableName}/by-inertdate")]
    [ProducesResponseType(typeof(TableSyncResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponseDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponseDto), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<TableSyncResult>> SyncTableByInertDate(
        string tableName,
        [FromQuery] int daysBack = 1,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_tableSyncService.IsConfigured(tableName))
            {
                return BadRequest(new ErrorResponseDto
                {
                    Error = $"Table '{tableName}' is not configured",
                    Message = $"Available tables: {string.Join(", ", _tableSyncService.TableNames)}"
                });
            }

            var dateFilter = _dateBasedSyncService.GenerateInertDateFilter(daysBack);
            _logger.LogInformation("Syncing table {TableName} with INERTDATE filter: {DateFilter}", tableName, dateFilter);

            var request = new TableSyncRequest { FilterCondition = dateFilter };
            var result = await _tableSyncService.SyncTableAsync(tableName, request, cancellationToken);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing table {TableName} by INERTDATE", tableName);
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponseDto
            {
                Error = "Sync operation failed",
                Message = ex.Message
            });
        }
    }

    /// <summary>
    /// Sync a table using only UPDATEDATE filtering (UPDATEDATE >= date-N).
    /// </summary>
    /// <param name="tableName">Name of the configured table to sync</param>
    /// <param name="daysBack">Number of days back from today (default 1 = yesterday)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Table sync result</returns>
    [HttpPost("table/{tableName}/by-updatedate")]
    [ProducesResponseType(typeof(TableSyncResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponseDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponseDto), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<TableSyncResult>> SyncTableByUpdateDate(
        string tableName,
        [FromQuery] int daysBack = 1,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_tableSyncService.IsConfigured(tableName))
            {
                return BadRequest(new ErrorResponseDto
                {
                    Error = $"Table '{tableName}' is not configured",
                    Message = $"Available tables: {string.Join(", ", _tableSyncService.TableNames)}"
                });
            }

            var dateFilter = _dateBasedSyncService.GenerateUpdateDateFilter(daysBack);
            _logger.LogInformation("Syncing table {TableName} with UPDATEDATE filter: {DateFilter}", tableName, dateFilter);

            var request = new TableSyncRequest { FilterCondition = dateFilter };
            var result = await _tableSyncService.SyncTableAsync(tableName, request, cancellationToken);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing table {TableName} by UPDATEDATE", tableName);
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponseDto
            {
                Error = "Sync operation failed",
                Message = ex.Message
            });
        }
    }

    /// <summary>
    /// Sync all configured tables using date-based filtering.
    /// Each table is synced with INERTDATE >= date-N OR UPDATEDATE >= date-N.
    /// </summary>
    /// <param name="daysBack">Number of days back from today (default 1 = yesterday)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Results for all synced tables</returns>
    [HttpPost("all-tables/by-date")]
    [ProducesResponseType(typeof(IReadOnlyList<TableSyncResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponseDto), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<IReadOnlyList<TableSyncResult>>> SyncAllTablesByDate(
        [FromQuery] int daysBack = 1,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Syncing all tables with dual-date filter (daysBack={DaysBack})", daysBack);

            var results = new List<TableSyncResult>();

            foreach (var tableName in _tableSyncService.TableNames)
            {
                try
                {
                    // Use LookbackDays to trigger DeltaSyncFilterBuilder which uses each table's configured date fields
                    var request = new TableSyncRequest { LookbackDays = daysBack };
                    var result = await _tableSyncService.SyncTableAsync(tableName, request, cancellationToken);
                    results.Add(result);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error syncing table {TableName}", tableName);
                    results.Add(new TableSyncResult
                    {
                        Name = tableName,
                        Success = false,
                        ErrorMessage = ex.Message,
                        ErrorType = SyncErrorTypes.SapRead
                    });
                }
            }

            return Ok(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing all tables");
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponseDto
            {
                Error = "Sync operation failed",
                Message = ex.Message
            });
        }
    }

    /// <summary>
    /// Get available sync date filter options and current configured tables.
    /// </summary>
    /// <returns>Info about available tables and filter options</returns>
    [HttpGet("info")]
    [ProducesResponseType(typeof(SyncInfoDto), StatusCodes.Status200OK)]
    public ActionResult<SyncInfoDto> GetSyncInfo()
    {
        var tomorrow = DateTime.Now.AddDays(1).ToString("yyyyMMdd");
        var today = DateTime.Now.ToString("yyyyMMdd");
        var yesterday = DateTime.Now.AddDays(-1).ToString("yyyyMMdd");
        var twoDaysAgo = DateTime.Now.AddDays(-2).ToString("yyyyMMdd");

        return Ok(new SyncInfoDto
        {
            AvailableTables = _tableSyncService.TableNames.ToList(),
            CurrentDateTime = DateTime.Now,
            AvailableDateFilters = new Dictionary<string, string>
            {
                ["daysBack=0"] = $"Today or later (SAP date >= {today})",
                ["daysBack=1"] = $"Yesterday or later (SAP date >= {yesterday})",
                ["daysBack=2"] = $"Two days ago or later (SAP date >= {twoDaysAgo})",
                ["daysBack=N"] = $"N days ago or later (SAP date >= YYYYMMDD)"
            }
        });
    }

    #region DTOs

    public sealed class ErrorResponseDto
    {
        public required string Error { get; set; }
        public string? Message { get; set; }
    }

    public sealed class SyncInfoDto
    {
        public required IReadOnlyList<string> AvailableTables { get; set; }
        public required DateTime CurrentDateTime { get; set; }
        public required Dictionary<string, string> AvailableDateFilters { get; set; }
    }

    #endregion
}
