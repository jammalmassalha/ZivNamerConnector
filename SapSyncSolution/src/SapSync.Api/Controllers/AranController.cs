using Microsoft.AspNetCore.Mvc;
using SapSync.Api.Services;
using SapSync.Core.Interfaces;
using SapSync.Core.Models;
using System.Globalization;

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
    // SAP tables pulled by /api/aran/sync, in dependency order (event, incident cases, movements, detailed movements).
    private static readonly string[] AranSapTables = { "ZNACCIDENT", "ZNMCICASE", "NBEW", "N_FALL_BEW" };

    private readonly AranSyncService _aranSyncService;
    private readonly ITableSyncService _tableSyncService;
    private readonly ILogger<AranController> _logger;
    private readonly string _sqlConnectionString;

    public AranController(
        AranSyncService aranSyncService,
        ITableSyncService tableSyncService,
        ILogger<AranController> logger,
        IConfiguration configuration)
    {
        _aranSyncService = aranSyncService ?? throw new ArgumentNullException(nameof(aranSyncService));
        _tableSyncService = tableSyncService ?? throw new ArgumentNullException(nameof(tableSyncService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _sqlConnectionString = configuration.GetConnectionString("SqlServer")
            ?? throw new InvalidOperationException("SqlServer connection string is not configured.");
    }

    [HttpGet("accidents/latest")]
    [ProducesResponseType(typeof(AranDashboardPayloadDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AranDashboardPayloadDto>> GetLatestAccidents(CancellationToken cancellationToken)
    {
        var incidents = new List<IncidentOptionDto>();
        await using (var conn = new Microsoft.Data.SqlClient.SqlConnection(_sqlConnectionString))
        {
            await conn.OpenAsync(cancellationToken);
            const string sql = @"
                SELECT TOP (10) ACCIDENT_NO, ACCIDENT_PLACE, ACCIDENT_DATE, ACCIDENT_TIME
                FROM [dbo].[ZNACCIDENT]
                ORDER BY ACCIDENT_DATE DESC, ACCIDENT_TIME DESC, ACCIDENT_NO DESC";
            await using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                incidents.Add(new IncidentOptionDto
                {
                    AccidentNo = reader.GetString(0).Trim(),
                    AccidentPlace = reader.IsDBNull(1) ? null : reader.GetString(1).Trim(),
                    AccidentDate = reader.IsDBNull(2) ? null : reader.GetDateTime(2),
                    AccidentTime = reader.IsDBNull(3) ? null : reader.GetTimeSpan(3)
                });
            }
        }

        var selectedIncident = incidents.FirstOrDefault();
        var cases = selectedIncident is null
            ? new List<AranPatientCaseDto>()
            : await QueryIncidentCasesAsync(selectedIncident.AccidentNo, cancellationToken);

        return Ok(new AranDashboardPayloadDto
        {
            Incidents = incidents,
            SelectedIncident = selectedIncident,
            Cases = cases
        });
    }

    [HttpGet("accidents/{accidentNo}/cases")]
    [ProducesResponseType(typeof(IEnumerable<AranPatientCaseDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<AranPatientCaseDto>>> GetAccidentCases(
        string accidentNo,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accidentNo))
            return BadRequest(new { error = "accidentNo is required" });

        return Ok(await QueryIncidentCasesAsync(accidentNo.Trim(), cancellationToken));
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
    /// Pulls ZNACCIDENT, ZNMCICASE, NBEW and N_FALL_BEW from SAP into SQL Server, then refreshes PATIENT_MONITORING.
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
            // 1) Pull the ARAN tables (including N_FALL_BEW) from SAP into SQL Server.
            var lookbackDays = request?.LookbackDays
                ?? (request?.FromDate is { } fromDate ? Math.Max(0, (DateTime.Today - fromDate.Date).Days) : 1);

            var tableResults = new List<AranTableSyncDto>();
            foreach (var table in AranSapTables)
            {
                if (!_tableSyncService.IsConfigured(table))
                {
                    tableResults.Add(new AranTableSyncDto { Name = table, Success = false, ErrorMessage = "Table is not configured in SyncTables." });
                    continue;
                }

                var r = await _tableSyncService.SyncTableAsync(
                    table, new TableSyncRequest { LookbackDays = lookbackDays }, cancellationToken);
                if (!r.Success)
                    _logger.LogError("ARAN SAP sync failed for {Table}: {Error}", table, r.ErrorMessage);

                tableResults.Add(new AranTableSyncDto
                {
                    Name = table,
                    Success = r.Success,
                    RowsRead = r.RowsRead,
                    RowsProcessed = r.RowsProcessed,
                    ErrorMessage = r.ErrorMessage
                });
            }

            // 2) Refresh the dbo.PATIENT_MONITORING cache from the freshly synced data (best effort).
            var (localOk, updatedCount, localError) = await _aranSyncService.SyncAranCasesAsync(
                request?.AccidentNo,
                request?.FromDate,
                cancellationToken);
            if (!localOk)
                _logger.LogWarning("PATIENT_MONITORING refresh failed: {Error}", localError);

            var failed = tableResults.Where(t => !t.Success).ToList();
            var result = new SyncAranResultDto
            {
                Success = failed.Count == 0,
                UpdatedCasesCount = updatedCount,
                ErrorMessage = failed.Count == 0
                    ? null
                    : string.Join("; ", failed.Select(t => $"{t.Name}: {t.ErrorMessage}")),
                SyncedAt = DateTime.UtcNow,
                Tables = tableResults
            };

            return result.Success ? Ok(result) : StatusCode(StatusCodes.Status502BadGateway, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ARAN sync failed");
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
                FROM [dbo].[N_FALL_BEW] n
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
            "1" or "T1" or "RED" => "RED",
            "2" or "T2" or "YELLOW" => "YELLOW",
            "3" or "T3" or "GREEN" => "GREEN",
            "4" or "BLACK" => "BLACK",
            _ => null
        };

    private async Task<List<AranPatientCaseDto>> QueryIncidentCasesAsync(
        string accidentNo,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT
                c.PATCASEID, f.PATIENTID, p.ID_NUMBER, p.FRST_NAME_PAT, p.LAST_NAME_PAT, p.DOB,
                c.MAIN_INJURY, c.MEDICAL_STATUS,
                m.MOVEMNT_SEQNO, m.MOVEMNT_CTGRY, m.MOVEMENT_TYPE, m.MOVEMNT_DATE, m.MOVEMNT_TIME,
                m.DEPARTMENT, m.NURS_TREAT_OU
            FROM [dbo].[ZNMCICASE] c
            INNER JOIN [dbo].[NFAL] f ON f.PATCASEID = c.PATCASEID
            LEFT JOIN [dbo].[NPAT] p ON p.PATIENTID = f.PATIENTID
            LEFT JOIN [dbo].[NBEW] m ON m.PATCASEID = c.PATCASEID
            WHERE LTRIM(RTRIM(c.ACCIDENT_NO)) = @AccidentNo
            ORDER BY c.PATCASEID, TRY_CONVERT(int, m.MOVEMNT_SEQNO), m.MOVEMNT_SEQNO";

        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(_sqlConnectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@AccidentNo", accidentNo);

        var cases = new List<AranPatientCaseDto>();
        var casesById = new Dictionary<string, AranPatientCaseDto>(StringComparer.Ordinal);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var caseId = reader.GetString(0).Trim();
            if (!casesById.TryGetValue(caseId, out var patientCase))
            {
                var medicalStatus = reader.IsDBNull(7) ? null : reader.GetString(7).Trim();
                patientCase = new AranPatientCaseDto
                {
                    CaseId = caseId,
                    PatientId = reader.IsDBNull(1) ? null : reader.GetString(1).Trim(),
                    NationalId = reader.IsDBNull(2) ? null : reader.GetString(2).Trim(),
                    FirstName = reader.IsDBNull(3) ? null : reader.GetString(3).Trim(),
                    LastName = reader.IsDBNull(4) ? null : reader.GetString(4).Trim(),
                    DateOfBirth = reader.IsDBNull(5) ? null : reader.GetDateTime(5),
                    MainInjury = reader.IsDBNull(6) ? null : reader.GetString(6).Trim(),
                    TriageCode = NormalizeTriage(medicalStatus),
                    TriageLabel = TriageLabel(NormalizeTriage(medicalStatus)),
                    AccidentNo = accidentNo
                };
                casesById.Add(caseId, patientCase);
                cases.Add(patientCase);
            }

            if (reader.IsDBNull(8))
                continue;

            var sequenceText = reader.GetString(8).Trim();
            int.TryParse(sequenceText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sequence);
            var movementCategory = reader.IsDBNull(9) ? null : reader.GetString(9).Trim();
            var movementTypeCode = reader.IsDBNull(10) ? null : reader.GetString(10).Trim();
            var movementDate = reader.IsDBNull(11) ? (DateTime?)null : reader.GetDateTime(11);
            var movementTime = reader.IsDBNull(12) ? (TimeSpan?)null : reader.GetTimeSpan(12);
            var department = reader.IsDBNull(13) ? null : reader.GetString(13).Trim();
            var careUnit = reader.IsDBNull(14) ? null : reader.GetString(14).Trim();
            var movement = new MovementHistoryItemDto
            {
                SequenceNumber = sequence,
                MovementCode = $"{movementCategory}|{movementTypeCode}",
                MovementType = SapSync.Core.Constants.SapMovementMap.TryGetMovementDescription(
                    movementCategory, movementTypeCode) ?? MovementLabel(movementCategory),
                Timestamp = CombineDateTime(movementDate, movementTime),
                MedicalDept = department,
                NursingUnit = careUnit
            };
            patientCase.Movements.Add(movement);
            patientCase.CurrentMovementCategory = movementCategory;
            patientCase.CurrentStatus = movement.MovementType;
            patientCase.Department = department;
            patientCase.Location = string.Join(" / ", new[] { department, careUnit }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            patientCase.LastMovementTime = movement.Timestamp;
        }

        return cases;
    }

    private static string? TriageLabel(string? triageCode) => triageCode switch
    {
        "RED" => "קשה / מיידי (T1)",
        "YELLOW" => "בינוני (T2)",
        "GREEN" => "קל (T3)",
        "BLACK" => "נפטר",
        _ => null
    };

    private static string? MovementLabel(string? category) => category switch
    {
        "1" => "קבלה",
        "2" => "שחרור",
        "3" => "העברה",
        _ => category
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
            FROM [dbo].[N_FALL_BEW] n
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

    public sealed class AranDashboardPayloadDto
    {
        public required IEnumerable<IncidentOptionDto> Incidents { get; set; }
        public IncidentOptionDto? SelectedIncident { get; set; }
        public required IEnumerable<AranPatientCaseDto> Cases { get; set; }
    }

    public sealed class IncidentOptionDto
    {
        public required string AccidentNo { get; set; }
        public string? AccidentPlace { get; set; }
        public DateTime? AccidentDate { get; set; }
        public TimeSpan? AccidentTime { get; set; }
    }

    public sealed class AranPatientCaseDto
    {
        public required string CaseId { get; set; }
        public string? PatientId { get; set; }
        public string? NationalId { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? FullName => string.Join(" ", new[] { FirstName, LastName }.Where(name => !string.IsNullOrWhiteSpace(name)));
        public DateTime? DateOfBirth { get; set; }
        public required string AccidentNo { get; set; }
        public string? MainInjury { get; set; }
        public string? TriageCode { get; set; }
        public string? TriageLabel { get; set; }
        public string? CurrentMovementCategory { get; set; }
        public string? CurrentStatus { get; set; }
        public string? Location { get; set; }
        public string? Department { get; set; }
        public DateTime? LastMovementTime { get; set; }
        public List<MovementHistoryItemDto> Movements { get; } = new();
    }

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

        /// <summary>Days back to pull from SAP (default 1). Overrides FromDate.</summary>
        public int? LookbackDays { get; set; }
    }

    public sealed class AranTableSyncDto
    {
        public required string Name { get; set; }
        public bool Success { get; set; }
        public int RowsRead { get; set; }
        public int RowsProcessed { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public sealed class SyncAranResultDto
    {
        public required bool Success { get; set; }
        public required int UpdatedCasesCount { get; set; }
        public string? ErrorMessage { get; set; }
        public required DateTime SyncedAt { get; set; }
        public IReadOnlyList<AranTableSyncDto> Tables { get; set; } = Array.Empty<AranTableSyncDto>();
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
