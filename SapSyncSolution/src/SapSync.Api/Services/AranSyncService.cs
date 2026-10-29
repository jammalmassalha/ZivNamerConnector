using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.SqlClient;
using SapSync.Core.Constants;
using SapSync.Api.Hubs;

namespace SapSync.Api.Services;

/// <summary>
/// High-performance batch-cached synchronization service for ARAN (אירוע רב נפגעים - Multi-Casualty Incident) cases.
/// Orchestrates data flow between SAP IS-H (ZMCI_TRANSFER, NFKL, TNKAT, N_FALL_BEW) and SQL Server (PATIENT_MONITORING).
/// Broadcasts real-time updates via SignalR hub.
/// </summary>
public sealed class AranSyncService
{
    private readonly string _sqlConnectionString;
    private readonly IHubContext<AranHub> _hubContext;

    public AranSyncService(string sqlConnectionString, IHubContext<AranHub> hubContext)
    {
        _sqlConnectionString = sqlConnectionString ?? throw new ArgumentNullException(nameof(sqlConnectionString));
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
    }

    /// <summary>
    /// Sync ARAN case data from SQL Server N_FALL_BEW and NFKL into PATIENT_MONITORING.
    /// Applies movement category/type dictionary mapping and broadcasts updates via SignalR.
    /// </summary>
    /// <param name="accidentNo">Optional: filter to specific incident; if null, syncs all incidents with recent movements</param>
    /// <param name="fromDate">Optional: only movements from this date forward</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Tuple of (success, updatedCaseCount, errorMessage)</returns>
    public async Task<(bool Success, int UpdatedCount, string? ErrorMessage)> SyncAranCasesAsync(
        string? accidentNo = null,
        DateTime? fromDate = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var conn = new SqlConnection(_sqlConnectionString);
            await conn.OpenAsync(cancellationToken);

            // Query active cases with latest movement and current location
            var caseUpdates = await FetchCaseMovementsAsync(conn, accidentNo, fromDate, cancellationToken);

            if (caseUpdates.Count == 0)
                return (true, 0, null);

            // Upsert to PATIENT_MONITORING
            await UpsertCasesAsync(conn, caseUpdates, cancellationToken);

            // Group by accident number and broadcast per-incident updates
            var updatesByIncident = caseUpdates.GroupBy(c => c.AccidentNo).ToList();
            foreach (var incidentGroup in updatesByIncident)
            {
                if (!string.IsNullOrEmpty(incidentGroup.Key))
                {
                    await _hubContext.Clients
                        .Group($"incident_{incidentGroup.Key}")
                        .SendAsync("ReceiveAranUpdates", new
                        {
                            AccidentNo = incidentGroup.Key,
                            Casualties = incidentGroup.Select(c => new
                            {
                                c.CaseNumber,
                                c.PatientID,
                                c.CurrentMovement,
                                c.TriageStatus,
                                c.PatientType,
                                c.CurrentLocation,
                                c.LastMovementTime
                            }),
                            UpdatedAt = DateTime.UtcNow
                        }, cancellationToken);

                    foreach (var update in incidentGroup.Where(c =>
                                 c.MovementChanged && (c.MovementCategory is "1" or "3")))
                    {
                        await _hubContext.Clients
                            .Group($"incident_{incidentGroup.Key}")
                            .SendAsync("ReceiveAranPatientUpdate", new
                            {
                                AccidentNo = incidentGroup.Key,
                                CaseId = update.CaseNumber,
                                PatientId = update.PatientID,
                                MovementCategory = update.MovementCategory,
                                CurrentStatus = update.CurrentMovement,
                                Location = update.CurrentLocation,
                                UpdatedAt = DateTime.UtcNow
                            }, cancellationToken);
                    }

                    // Also send KPI summary for this incident
                    var kpi = CalculateKpi(incidentGroup);
                    await _hubContext.Clients
                        .Group($"incident_{incidentGroup.Key}")
                        .SendAsync("ReceiveKpiUpdate", new
                        {
                            AccidentNo = incidentGroup.Key,
                            Kpi = kpi,
                            UpdatedAt = DateTime.UtcNow
                        }, cancellationToken);
                }
            }

            return (true, caseUpdates.Count, null);
        }
        catch (Exception ex)
        {
            return (false, 0, ex.Message);
        }
    }

    /// <summary>
    /// Fetch active ARAN cases with their latest movements and current locations from SQL Server.
    /// </summary>
    private async Task<List<CaseUpdateModel>> FetchCaseMovementsAsync(
        SqlConnection conn,
        string? filterAccidentNo,
        DateTime? fromDate,
        CancellationToken cancellationToken)
    {
        var results = new List<CaseUpdateModel>();

        // Build dynamic WHERE clause
        var whereConditions = new List<string> { "n.STORN = ' ' AND n.FSTORN = ' '" };
        if (!string.IsNullOrEmpty(filterAccidentNo))
            whereConditions.Add("EXISTS (SELECT 1 FROM [dbo].[ZNMCICASE] z WHERE z.PATCASEID = n.FALNR AND z.ACCIDENT_NO = @FilterAccidentNo)");
        if (fromDate.HasValue)
            whereConditions.Add($"n.BWIDT >= @FromDate");

        var whereClause = string.Join(" AND ", whereConditions);

        var sql = $@"
            SELECT DISTINCT
                n.FALNR AS CaseNumber,
                n.PATNR AS PatientID,
                n.BEWTY,
                n.BWART,
                n.FALAR AS CaseType,
                n.CLASS AS PatientTypeCode,
                n.BWIDT AS MovementDate,
                n.BWITM AS MovementTime,
                n.ZIMMR AS Room,
                n.BETT AS Bed,
                COALESCE(zc.ACCIDENT_NO, nfkl.AccidentNo) AS AccidentNo,
                nfkl.TriageStatus,
                nfkl.CurrentMovementCode AS PreviousMovementCode,
                CASE WHEN nfkl.CaseNumber IS NULL OR COALESCE(nfkl.CurrentMovementCode, '') <> CONCAT(n.BEWTY, '|', n.BWART)
                     THEN 1 ELSE 0 END AS MovementChanged,
                CASE 
                    WHEN ROW_NUMBER() OVER (PARTITION BY n.FALNR ORDER BY n.BWIDT DESC, n.LFDBEW DESC) = 1 
                    THEN 1 ELSE 0 
                END AS IsLatestMovement
            FROM [dbo].[N_FALL_BEW] n
            OUTER APPLY (
                SELECT TOP (1) z.ACCIDENT_NO
                FROM [dbo].[ZNMCICASE] z
                WHERE z.PATCASEID = n.FALNR
                ORDER BY z.ACCIDENT_NO
            ) zc
            LEFT JOIN [dbo].[PATIENT_MONITORING] nfkl ON n.FALNR = nfkl.CaseNumber
            WHERE {whereClause}
            ORDER BY n.BWIDT DESC, n.LFDBEW DESC";

        using var cmd = new SqlCommand(sql, conn);
        if (!string.IsNullOrEmpty(filterAccidentNo))
            cmd.Parameters.AddWithValue("@FilterAccidentNo", filterAccidentNo);
        if (fromDate.HasValue)
            cmd.Parameters.AddWithValue("@FromDate", fromDate.Value.Date);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var isLatest = reader.GetInt32(reader.GetOrdinal("IsLatestMovement")) == 1;
            if (!isLatest)
                continue;

            var bewty = reader.IsDBNull(reader.GetOrdinal("BEWTY")) ? null : reader.GetString(reader.GetOrdinal("BEWTY"));
            var bwart = reader.IsDBNull(reader.GetOrdinal("BWART")) ? null : reader.GetString(reader.GetOrdinal("BWART"));
            var classCode = reader.IsDBNull(reader.GetOrdinal("PatientTypeCode")) ? null : reader.GetString(reader.GetOrdinal("PatientTypeCode"));
            var movementCategory = reader.IsDBNull(reader.GetOrdinal("BEWTY")) ? null : reader.GetString(reader.GetOrdinal("BEWTY")).Trim();

            var movementDesc = SapMovementMap.TryGetMovementDescription(bewty, bwart);
            var patientTypeDesc = SapMovementMap.TryGetPatientType(classCode);

            var room = reader.IsDBNull(reader.GetOrdinal("Room")) ? null : reader.GetString(reader.GetOrdinal("Room"));
            var bed = reader.IsDBNull(reader.GetOrdinal("Bed")) ? null : reader.GetString(reader.GetOrdinal("Bed"));
            var location = BuildLocationString(room, bed);

            var movementDate = reader.IsDBNull(reader.GetOrdinal("MovementDate")) ? (DateTime?)null : reader.GetDateTime(reader.GetOrdinal("MovementDate"));
            var movementTime = reader.IsDBNull(reader.GetOrdinal("MovementTime")) ? (TimeSpan?)null : reader.GetTimeSpan(reader.GetOrdinal("MovementTime"));
            var lastMovementTime = CombineDateTime(movementDate, movementTime);

            results.Add(new CaseUpdateModel
            {
                CaseNumber = reader.GetString(reader.GetOrdinal("CaseNumber")),
                PatientID = reader.IsDBNull(reader.GetOrdinal("PatientID")) ? null : reader.GetString(reader.GetOrdinal("PatientID")),
                AccidentNo = reader.IsDBNull(reader.GetOrdinal("AccidentNo")) ? null : reader.GetString(reader.GetOrdinal("AccidentNo")),
                CurrentMovement = movementDesc,
                CurrentMovementCode = $"{bewty}|{bwart}",
                MovementCategory = movementCategory,
                MovementChanged = reader.GetInt32(reader.GetOrdinal("MovementChanged")) == 1,
                TriageStatus = reader.IsDBNull(reader.GetOrdinal("TriageStatus")) ? null : reader.GetString(reader.GetOrdinal("TriageStatus")),
                PatientType = patientTypeDesc,
                CurrentLocation = location,
                LastMovementTime = lastMovementTime,
                CaseType = reader.IsDBNull(reader.GetOrdinal("CaseType")) ? null : reader.GetString(reader.GetOrdinal("CaseType"))
            });
        }

        return results;
    }

    /// <summary>
    /// Upsert (merge) updated case data into PATIENT_MONITORING table.
    /// </summary>
    private async Task UpsertCasesAsync(SqlConnection conn, List<CaseUpdateModel> caseUpdates, CancellationToken cancellationToken)
    {
        const string mergeSql = @"
            MERGE INTO [dbo].[PATIENT_MONITORING] AS target
            USING (
                SELECT 
                    @CaseNumber AS CaseNumber,
                    @AccidentNo AS AccidentNo,
                    @PatientID AS PatientID,
                    @CaseType AS CaseType,
                    @CurrentMovement AS CurrentMovement,
                    @CurrentMovementCode AS CurrentMovementCode,
                    @CurrentLocation AS CurrentLocation,
                    @TriageStatus AS TriageStatus,
                    @PatientType AS PatientType,
                    @LastMovementTime AS LastMovementTime
            ) AS source
            ON target.CaseNumber = source.CaseNumber
            WHEN MATCHED THEN
                UPDATE SET
                    AccidentNo = source.AccidentNo,
                    PatientID = source.PatientID,
                    CaseType = source.CaseType,
                    CurrentMovement = source.CurrentMovement,
                    CurrentMovementCode = source.CurrentMovementCode,
                    CurrentLocation = source.CurrentLocation,
                    TriageStatus = COALESCE(source.TriageStatus, target.TriageStatus),
                    PatientType = source.PatientType,
                    LastMovementTime = source.LastMovementTime,
                    UpdatedAt = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN
                INSERT (CaseNumber, AccidentNo, PatientID, CaseType, CurrentMovement, CurrentMovementCode, CurrentLocation, TriageStatus, PatientType, LastMovementTime, UpdatedAt)
                VALUES (source.CaseNumber, source.AccidentNo, source.PatientID, source.CaseType, source.CurrentMovement, source.CurrentMovementCode, source.CurrentLocation, source.TriageStatus, source.PatientType, source.LastMovementTime, SYSUTCDATETIME());";

        using var cmd = new SqlCommand(mergeSql, conn);

        foreach (var update in caseUpdates)
        {
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@CaseNumber", update.CaseNumber);
            cmd.Parameters.AddWithValue("@AccidentNo", (object?)update.AccidentNo ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@PatientID", (object?)update.PatientID ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CaseType", (object?)update.CaseType ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CurrentMovement", (object?)update.CurrentMovement ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CurrentMovementCode", (object?)update.CurrentMovementCode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CurrentLocation", (object?)update.CurrentLocation ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@TriageStatus", (object?)update.TriageStatus ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@PatientType", (object?)update.PatientType ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@LastMovementTime", (object?)update.LastMovementTime ?? DBNull.Value);

            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Calculate KPI summary from a group of cases (counts by triage status, in-treatment, discharged).
    /// </summary>
    private static object CalculateKpi(IGrouping<string?, CaseUpdateModel> caseGroup)
    {
        var total = caseGroup.Count();
        var red = caseGroup.Count(c => c.TriageStatus == SapMovementMap.TriageStatuses.Red);
        var yellow = caseGroup.Count(c => c.TriageStatus == SapMovementMap.TriageStatuses.Yellow);
        var green = caseGroup.Count(c => c.TriageStatus == SapMovementMap.TriageStatuses.Green);
        var black = caseGroup.Count(c => c.TriageStatus == SapMovementMap.TriageStatuses.Black);
        var inTreatment = caseGroup.Count(c => c.CurrentMovement?.Contains("אשפוז") == true || c.CaseType == "1");
        var discharged = total - inTreatment - black;

        return new
        {
            Total = total,
            Red = "red",
            Yellow = "yellow",
            Green = "green",
            Black = "black",
            InTreatment = inTreatment,
            Discharged = discharged
        };
    }

    private static string? BuildLocationString(string? room, string? bed)
    {
        if (string.IsNullOrEmpty(room) && string.IsNullOrEmpty(bed))
            return null;

        return string.IsNullOrEmpty(bed) 
            ? $"חדר {room}" 
            : $"חדר {room}, מיטה {bed}";
    }

    private static DateTime? CombineDateTime(DateTime? date, TimeSpan? time)
    {
        if (!date.HasValue)
            return null;

        if (!time.HasValue)
            return date;

        return date.Value.Date.Add(time.Value);
    }

    /// <summary>
    /// DTO for case update model.
    /// </summary>
    private sealed class CaseUpdateModel
    {
        public required string CaseNumber { get; set; }
        public string? PatientID { get; set; }
        public string? AccidentNo { get; set; }
        public string? CurrentMovement { get; set; }
        public string? CurrentMovementCode { get; set; }
        public string? MovementCategory { get; set; }
        public bool MovementChanged { get; set; }
        public string? CurrentLocation { get; set; }
        public string? TriageStatus { get; set; }
        public string? PatientType { get; set; }
        public DateTime? LastMovementTime { get; set; }
        public string? CaseType { get; set; }
    }
}
