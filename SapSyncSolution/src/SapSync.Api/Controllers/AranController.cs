using Microsoft.AspNetCore.Mvc;
using SapSync.Api.Services;

namespace SapSync.Api.Controllers;

/// <summary>
/// REST API endpoints for ARAN (אירוע רב נפגעים - Multi-Casualty Incident) management.
/// Provides operations to query, sync, and update casualty cases during mass-casualty events.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public sealed class AranController : ControllerBase
{
    private readonly AranSyncService _aranSyncService;
    private readonly string _sqlConnectionString;

    public AranController(AranSyncService aranSyncService, IConfiguration configuration)
    {
        _aranSyncService = aranSyncService ?? throw new ArgumentNullException(nameof(aranSyncService));
        _sqlConnectionString = configuration.GetConnectionString("SqlServer") 
            ?? throw new InvalidOperationException("SqlServer connection string is not configured.");
    }

    /// <summary>
    /// Retrieve all active casualties for a specific incident, with optional search filtering.
    /// </summary>
    /// <param name="accidentNo">The incident/accident number (UNFNR) - required</param>
    /// <param name="search">Optional: search term to filter by case number, patient ID, or location</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of casualty records</returns>
    [HttpGet("cases")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IEnumerable<CasualtyDto>>> GetCases(
        [FromQuery] string? accidentNo,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accidentNo))
            return BadRequest(new { error = "accidentNo is required" });

        var casualties = await QueryCasualtiesAsync(accidentNo, search, cancellationToken);
        return Ok(casualties);
    }

    /// <summary>
    /// Polling endpoint: returns casualties and KPIs for an incident in a single POST.
    /// </summary>
    [HttpPost("poll")]
    [ProducesResponseType(typeof(AranPollResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AranPollResultDto>> Poll(
        [FromBody] AranPollRequestDto? request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.AccidentNo))
            return BadRequest(new { error = "accidentNo is required" });

        var casualties = await QueryCasualtiesAsync(request.AccidentNo, request.Search, cancellationToken);
        var kpi = await QueryKpisAsync(request.AccidentNo, cancellationToken);
        return Ok(new AranPollResultDto
        {
            Casualties = casualties,
            Kpi = kpi,
            ServerTime = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Retrieve KPI summary (counts by triage status) for a specific incident.
    /// </summary>
    /// <param name="accidentNo">The incident number - required</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>KPI object with counts</returns>
    [HttpGet("kpis")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<KpiSummaryDto>> GetKpis(
        [FromQuery] string? accidentNo,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accidentNo))
            return BadRequest(new { error = "accidentNo is required" });

        var kpi = await QueryKpisAsync(accidentNo, cancellationToken);
        return Ok(kpi);
    }

    /// <summary>
    /// Manually trigger a sync of ARAN case data from SAP N_FALL_BEW to PATIENT_MONITORING.
    /// Broadcasts updates to all connected clients via SignalR for the affected incident.
    /// </summary>
    /// <param name="request">Optional: filter parameters (accidentNo, fromDate)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Sync result</returns>
    [HttpPost("sync")]
    [ProducesResponseType(typeof(SyncAranResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SyncAranResultDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(SyncAranResultDto), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<SyncAranResultDto>> SyncAran(
        [FromBody] SyncAranRequestDto? request,
        CancellationToken cancellationToken)
    {
        try
        {
            var (success, updatedCount, errorMessage) = await _aranSyncService.SyncAranCasesAsync(
                request?.AccidentNo,
                request?.FromDate,
                cancellationToken);

            var result = new SyncAranResultDto
            {
                Success = success,
                UpdatedCasesCount = updatedCount,
                ErrorMessage = errorMessage,
                SyncedAt = DateTime.UtcNow
            };

            return success ? Ok(result) : StatusCode(StatusCodes.Status500InternalServerError, result);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new SyncAranResultDto
            {
                Success = false,
                UpdatedCasesCount = 0,
                ErrorMessage = ex.Message,
                SyncedAt = DateTime.UtcNow
            });
        }
    }

    /// <summary>
    /// Update manual triage status or notes for a specific patient case.
    /// Triggers an update broadcast to all connected clients monitoring the incident.
    /// </summary>
    /// <param name="caseNumber">The case/patient number (FALNR)</param>
    /// <param name="request">Update request with new triage status</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated patient record</returns>
    [HttpPut("patient/{caseNumber}/triage")]
    [ProducesResponseType(typeof(CasualtyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<CasualtyDto>> UpdateTriageStatus(
        string caseNumber,
        [FromBody] UpdateTriageRequestDto request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(caseNumber))
            return BadRequest(new { error = "caseNumber is required" });

        if (string.IsNullOrWhiteSpace(request?.TriageStatus))
            return BadRequest(new { error = "triageStatus is required" });

        try
        {
            var updated = await UpdateTriageAsync(caseNumber, request.TriageStatus, request.Notes, cancellationToken);
            if (updated == null)
                return NotFound();

            return Ok(updated);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Retrieve detailed movement history for a specific patient case.
    /// </summary>
    /// <param name="caseNumber">The case number (FALNR)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Detailed movement history</returns>
    [HttpGet("patient/{caseNumber}/history")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PatientHistoryDto>> GetPatientHistory(
        string caseNumber,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(caseNumber))
            return BadRequest(new { error = "caseNumber is required" });

        var history = await QueryPatientHistoryAsync(caseNumber, cancellationToken);
        return Ok(history);
    }

    /// <summary>
    /// Reads casualties live from the SAP-synced tables (ZNMCICASE / NBEW for the incident's cases,
    /// sap.N_FALL_BEW for the latest movement). dbo.PATIENT_MONITORING is only used as an optional
    /// manual triage override, so the dashboard works even if that table is empty.
    /// </summary>
    private async Task<List<CasualtyDto>> QueryCasualtiesAsync(
        string accidentNo,
        string? search,
        CancellationToken cancellationToken)
    {
        using var conn = new Microsoft.Data.SqlClient.SqlConnection(_sqlConnectionString);
        await conn.OpenAsync(cancellationToken);

        var sql = @"
            ;WITH IncidentCases AS (
                SELECT PATCASEID, MAX(MEDICAL_STATUS) AS MEDICAL_STATUS
                FROM (
                    SELECT PATCASEID, MEDICAL_STATUS FROM [dbo].[ZNMCICASE] WHERE LTRIM(RTRIM(ACCIDENT_NO)) = @AccidentNo
                    UNION ALL
                    SELECT PATCASEID, NULL FROM [dbo].[NBEW] WHERE LTRIM(RTRIM(ACCIDENT_NO)) = @AccidentNo
                ) x
                GROUP BY PATCASEID
            ),
            LatestMovement AS (
                SELECT n.FALNR, n.PATNR, n.BEWTY, n.BWART, n.FALAR, n.CLASS, n.BWIDT, n.BWITM, n.ZIMMR, n.BETT,
                       ROW_NUMBER() OVER (PARTITION BY n.FALNR ORDER BY n.BWIDT DESC, n.BWITM DESC, n.LFDBEW DESC) AS rn
                FROM [sap].[N_FALL_BEW] n
                WHERE ISNULL(n.STORN, '') = '' AND ISNULL(n.FSTORN, '') = ''
            )
            SELECT c.PATCASEID, lm.PATNR, lm.BEWTY, lm.BWART, lm.FALAR, lm.CLASS, lm.BWIDT, lm.BWITM,
                   lm.ZIMMR, lm.BETT, pm.TriageStatus, c.MEDICAL_STATUS
            FROM IncidentCases c
            LEFT JOIN LatestMovement lm ON lm.FALNR = c.PATCASEID AND lm.rn = 1
            LEFT JOIN [dbo].[PATIENT_MONITORING] pm ON pm.CaseNumber = c.PATCASEID";

        if (!string.IsNullOrWhiteSpace(search))
        {
            sql += @"
            WHERE c.PATCASEID LIKE @Search OR lm.PATNR LIKE @Search OR lm.ZIMMR LIKE @Search";
        }

        sql += " ORDER BY lm.BWIDT DESC, lm.BWITM DESC";

        using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@AccidentNo", accidentNo.Trim());
        if (!string.IsNullOrWhiteSpace(search))
            cmd.Parameters.AddWithValue("@Search", $"%{search}%");

        static string? Str(System.Data.Common.DbDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i).Trim();

        var result = new List<CasualtyDto>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var bewty = Str(reader, 2);
            var bwart = Str(reader, 3);
            var room = Str(reader, 8);
            var bed = Str(reader, 9);
            var date = reader.IsDBNull(6) ? (DateTime?)null : reader.GetDateTime(6);
            var time = reader.IsDBNull(7) ? (TimeSpan?)null : reader.GetTimeSpan(7);
            var movementTime = CombineDateTime(date, time);

            result.Add(new CasualtyDto
            {
                CaseNumber = reader.GetString(0).Trim(),
                PatientID = Str(reader, 1),
                AccidentNo = accidentNo.Trim(),
                CaseType = Str(reader, 4),
                CurrentMovement = SapSync.Core.Constants.SapMovementMap.TryGetMovementDescription(bewty, bwart),
                CurrentLocation = string.Join(" / ", new[] { room, bed }.Where(x => !string.IsNullOrWhiteSpace(x))),
                TriageStatus = NormalizeTriage(Str(reader, 10) ?? Str(reader, 11)),
                PatientType = SapSync.Core.Constants.SapMovementMap.TryGetPatientType(Str(reader, 5)),
                LastMovementTime = movementTime,
                UpdatedAt = movementTime ?? DateTime.UtcNow
            });
        }

        return result;
    }

    private static string? NormalizeTriage(string? value) =>
        value?.Trim().ToUpperInvariant() switch
        {
            "RED" => "RED",
            "YELLOW" => "YELLOW",
            "GREEN" => "GREEN",
            "BLACK" => "BLACK",
            _ => null
        };

    private async Task<KpiSummaryDto?> QueryKpisAsync(string accidentNo, CancellationToken cancellationToken)
    {
        var cases = await QueryCasualtiesAsync(accidentNo, null, cancellationToken);
        var red = cases.Count(c => c.TriageStatus == "RED");
        var yellow = cases.Count(c => c.TriageStatus == "YELLOW");
        var green = cases.Count(c => c.TriageStatus == "GREEN");
        var black = cases.Count(c => c.TriageStatus == "BLACK");
        var inTreatment = cases.Count(c => c.CurrentMovement != null && c.CurrentMovement.Contains("אשפוז"));

        return new KpiSummaryDto
        {
            Total = cases.Count,
            Red = red,
            Yellow = yellow,
            Green = green,
            Black = black,
            InTreatment = inTreatment,
            Discharged = Math.Max(0, cases.Count - inTreatment - black)
        };
    }

    private async Task<CasualtyDto?> UpdateTriageAsync(string caseNumber, string triageStatus, string? notes, CancellationToken cancellationToken)
    {
        using var conn = new Microsoft.Data.SqlClient.SqlConnection(_sqlConnectionString);
        await conn.OpenAsync(cancellationToken);

        const string sql = @"
            UPDATE [dbo].[PATIENT_MONITORING]
            SET TriageStatus = @TriageStatus, UpdatedAt = SYSUTCDATETIME()
            WHERE CaseNumber = @CaseNumber;
            
            SELECT 
                CaseNumber, PatientID, AccidentNo, CaseType, CurrentMovement, 
                CurrentMovementCode, CurrentLocation, TriageStatus, PatientType, 
                LastMovementTime, UpdatedAt
            FROM [dbo].[PATIENT_MONITORING]
            WHERE CaseNumber = @CaseNumber";

        using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@CaseNumber", caseNumber);
        cmd.Parameters.AddWithValue("@TriageStatus", triageStatus);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new CasualtyDto
        {
            CaseNumber = reader.GetString(0),
            PatientID = reader.IsDBNull(1) ? null : reader.GetString(1),
            AccidentNo = reader.IsDBNull(2) ? null : reader.GetString(2),
            CaseType = reader.IsDBNull(3) ? null : reader.GetString(3),
            CurrentMovement = reader.IsDBNull(4) ? null : reader.GetString(4),
            CurrentLocation = reader.IsDBNull(6) ? null : reader.GetString(6),
            TriageStatus = reader.IsDBNull(7) ? null : reader.GetString(7),
            PatientType = reader.IsDBNull(8) ? null : reader.GetString(8),
            LastMovementTime = reader.IsDBNull(9) ? (DateTime?)null : reader.GetDateTime(9),
            UpdatedAt = reader.GetDateTime(10)
        };
    }

    private async Task<PatientHistoryDto> QueryPatientHistoryAsync(string caseNumber, CancellationToken cancellationToken)
    {
        using var conn = new Microsoft.Data.SqlClient.SqlConnection(_sqlConnectionString);
        await conn.OpenAsync(cancellationToken);

        const string sql = @"
            SELECT 
                n.FALNR, n.LFDBEW, n.BEWTY, n.BWART, n.BWIDT, n.BWITM,
                n.ZIMMR, n.BETT, n.ORGFA, n.ORGPF
            FROM [sap].[N_FALL_BEW] n
            WHERE n.FALNR = @CaseNumber AND n.STORN = ' ' AND n.FSTORN = ' '
            ORDER BY n.LFDBEW DESC";

        using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@CaseNumber", caseNumber);

        var movements = new List<MovementHistoryItemDto>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var bewty = reader.IsDBNull(2) ? null : reader.GetString(2);
            var bwart = reader.IsDBNull(3) ? null : reader.GetString(3);
            var date = reader.IsDBNull(4) ? (DateTime?)null : reader.GetDateTime(4);
            var time = reader.IsDBNull(5) ? (TimeSpan?)null : reader.GetTimeSpan(5);

            movements.Add(new MovementHistoryItemDto
            {
                SequenceNumber = reader.GetInt32(1),
                MovementType = SapSync.Core.Constants.SapMovementMap.TryGetMovementDescription(bewty, bwart),
                MovementCode = $"{bewty}|{bwart}",
                Timestamp = CombineDateTime(date, time),
                Room = reader.IsDBNull(6) ? null : reader.GetString(6),
                Bed = reader.IsDBNull(7) ? null : reader.GetString(7),
                MedicalDept = reader.IsDBNull(8) ? null : reader.GetString(8),
                NursingUnit = reader.IsDBNull(9) ? null : reader.GetString(9)
            });
        }

        return new PatientHistoryDto
        {
            CaseNumber = caseNumber,
            Movements = movements
        };
    }

    private static DateTime? CombineDateTime(DateTime? date, TimeSpan? time)
    {
        if (!date.HasValue)
            return null;
        return !time.HasValue ? date : date.Value.Date.Add(time.Value);
    }

    #region DTOs

    public sealed class AranPollRequestDto
    {
        public string? AccidentNo { get; set; }
        public string? Search { get; set; }
    }

    public sealed class AranPollResultDto
    {
        public required IEnumerable<CasualtyDto> Casualties { get; set; }
        public KpiSummaryDto? Kpi { get; set; }
        public DateTime ServerTime { get; set; }
    }

    public sealed class CasualtyDto
    {
        public required string CaseNumber { get; set; }
        public string? PatientID { get; set; }
        public string? AccidentNo { get; set; }
        public string? CaseType { get; set; }
        public string? CurrentMovement { get; set; }
        public string? CurrentLocation { get; set; }
        public string? TriageStatus { get; set; }
        public string? PatientType { get; set; }
        public DateTime? LastMovementTime { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public sealed class KpiSummaryDto
    {
        public int Total { get; set; }
        public int Red { get; set; }
        public int Yellow { get; set; }
        public int Green { get; set; }
        public int Black { get; set; }
        public int InTreatment { get; set; }
        public int Discharged { get; set; }
    }

    public sealed class SyncAranRequestDto
    {
        public string? AccidentNo { get; set; }
        public DateTime? FromDate { get; set; }
    }

    public sealed class SyncAranResultDto
    {
        public required bool Success { get; set; }
        public required int UpdatedCasesCount { get; set; }
        public string? ErrorMessage { get; set; }
        public required DateTime SyncedAt { get; set; }
    }

    public sealed class UpdateTriageRequestDto
    {
        public required string TriageStatus { get; set; }
        public string? Notes { get; set; }
    }

    public sealed class PatientHistoryDto
    {
        public required string CaseNumber { get; set; }
        public required IEnumerable<MovementHistoryItemDto> Movements { get; set; }
    }

    public sealed class MovementHistoryItemDto
    {
        public required int SequenceNumber { get; set; }
        public string? MovementType { get; set; }
        public string? MovementCode { get; set; }
        public DateTime? Timestamp { get; set; }
        public string? Room { get; set; }
        public string? Bed { get; set; }
        public string? MedicalDept { get; set; }
        public string? NursingUnit { get; set; }
    }

    #endregion
}
