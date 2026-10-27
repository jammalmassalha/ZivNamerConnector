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

    public SapSyncController(ISapSyncOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
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
}
