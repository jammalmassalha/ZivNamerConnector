using Microsoft.AspNetCore.Mvc;
using SapSync.Core.Interfaces;
using SapSync.Core.Models;

namespace SapSync.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public sealed class SapSyncController : ControllerBase
{
    private readonly ISapSyncOrchestrator _orchestrator;
    private readonly ITableSyncService _tableSync;

    public SapSyncController(ISapSyncOrchestrator orchestrator, ITableSyncService tableSync)
    {
        _orchestrator = orchestrator;
        _tableSync = tableSync;
    }

    /// <summary>Reads an SAP table via RFC_READ_TABLE and upserts it into a SQL Server table.</summary>
    [HttpPost("sync")]
    [ProducesResponseType(typeof(SyncResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SyncResult), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(SyncResult), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(SyncResult), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<SyncResult>> Sync([FromBody] SapQueryRequest request, CancellationToken cancellationToken)
    {
        var result = await _orchestrator.SyncAsync(request, cancellationToken);

        if (result.Success)
        {
            return Ok(result);
        }

        return result.ErrorType switch
        {
            SyncErrorTypes.Validation => BadRequest(result),
            SyncErrorTypes.SapRead => StatusCode(StatusCodes.Status502BadGateway, result),
            _ => StatusCode(StatusCodes.Status500InternalServerError, result),
        };
    }

    /// <summary>Lists the tables configured in the "SyncTables" catalog (NPAT, NFAL, NBEW, ZNACCIDENT, ZNMCICASE, ...).</summary>
    [HttpGet("tables")]
    [ProducesResponseType(typeof(IEnumerable<string>), StatusCodes.Status200OK)]
    public ActionResult<IEnumerable<string>> GetTables() => Ok(_tableSync.TableNames);

    /// <summary>Syncs one configured table page by page. The body is optional (filter / row limit overrides).</summary>
    [HttpPost("tables/{name}/sync")]
    [ProducesResponseType(typeof(TableSyncResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(TableSyncResult), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(TableSyncResult), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(TableSyncResult), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<TableSyncResult>> SyncTable(
        string name, [FromBody] TableSyncRequest? request, CancellationToken cancellationToken)
    {
        if (!_tableSync.IsConfigured(name))
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Table is not configured.");
        }

        var result = await _tableSync.SyncTableAsync(name, request, cancellationToken);
        return result.Success ? Ok(result) : ToErrorResult(result.ErrorType, result);
    }

    /// <summary>Syncs every configured atable. Returns 200 when all succeed, otherwise 207 with per-table results.</summary>
    [HttpPost("tables/sync-all")]
    [ProducesResponseType(typeof(IReadOnlyList<TableSyncResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(IReadOnlyList<TableSyncResult>), StatusCodes.Status207MultiStatus)]
    public async Task<ActionResult<IReadOnlyList<TableSyncResult>>> SyncAll(CancellationToken cancellationToken)
    {
        var results = await _tableSync.SyncAllAsync(cancellationToken);
        return results.All(r => r.Success) ? Ok(results) : StatusCode(StatusCodes.Status207MultiStatus, results);
    }

    private ObjectResult ToErrorResult(string? errorType, object body) => errorType switch
    {
        SyncErrorTypes.Validation => BadRequest(body),
        SyncErrorTypes.SapRead => StatusCode(StatusCodes.Status502BadGateway, body),
        _ => StatusCode(StatusCodes.Status500InternalServerError, body),
    };
}
