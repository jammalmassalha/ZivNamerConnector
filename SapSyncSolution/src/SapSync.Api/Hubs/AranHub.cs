using Microsoft.AspNetCore.SignalR;

namespace SapSync.Api.Hubs;

/// <summary>
/// Real-time SignalR hub for ARAN (אירוע רב נפגעים - Multi-Casualty Incident) casualty monitoring.
/// Clients join groups by accident/incident number to receive live updates on patient movements, triage status, and vital counts.
/// </summary>
public sealed class AranHub : Hub
{
    private const string IncidentGroupPrefix = "incident_";

    /// <summary>
    /// Called by client to join a specific incident group and receive real-time updates.
    /// </summary>
    /// <param name="accidentNo">The SAP accident/incident number (UNFNR from ZNACCIDENT)</param>
    /// <returns>Task representing the async operation</returns>
    public async Task JoinIncidentGroup(string accidentNo)
    {
        if (string.IsNullOrWhiteSpace(accidentNo))
            throw new ArgumentException("Accident number cannot be empty.", nameof(accidentNo));

        var groupName = $"{IncidentGroupPrefix}{accidentNo.Trim()}";
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

        await Clients.Group(groupName).SendAsync("NotifyGroupJoined", new
        {
            ConnectionId = Context.ConnectionId,
            AccidentNo = accidentNo,
            JoinedAt = DateTime.UtcNow,
            Message = $"Usuario conectado al incidente {accidentNo}"
        });
    }

    /// <summary>
    /// Called by client to leave an incident group.
    /// </summary>
    /// <param name="accidentNo">The SAP accident/incident number</param>
    /// <returns>Task representing the async operation</returns>
    public async Task LeaveIncidentGroup(string accidentNo)
    {
        if (string.IsNullOrWhiteSpace(accidentNo))
            throw new ArgumentException("Accident number cannot be empty.", nameof(accidentNo));

        var groupName = $"{IncidentGroupPrefix}{accidentNo.Trim()}";
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);

        await Clients.Group(groupName).SendAsync("NotifyGroupLeft", new
        {
            ConnectionId = Context.ConnectionId,
            AccidentNo = accidentNo,
            LeftAt = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Broadcast a full list of updated casualties for a specific incident.
    /// Called by AranSyncService after syncing patient movements.
    /// </summary>
    /// <param name="accidentNo">The incident number</param>
    /// <param name="casualtyList">List of updated casualty records (IEnumerable of dynamic or typed DTO)</param>
    /// <returns>Task representing the async operation</returns>
    public async Task BroadcastCasualtyUpdate(string accidentNo, IEnumerable<object> casualtyList)
    {
        if (string.IsNullOrWhiteSpace(accidentNo))
            return;

        var groupName = $"{IncidentGroupPrefix}{accidentNo.Trim()}";
        await Clients.Group(groupName).SendAsync("ReceiveAranUpdates", new
        {
            AccidentNo = accidentNo,
            Casualties = casualtyList,
            UpdatedAt = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Broadcast a KPI summary (counts) for a specific incident.
    /// </summary>
    /// <param name="accidentNo">The incident number</param>
    /// <param name="kpiSummary">Object containing total, red, yellow, green, black counts and in-treatment, discharged counts</param>
    /// <returns>Task representing the async operation</returns>
    public async Task BroadcastKpiUpdate(string accidentNo, object kpiSummary)
    {
        if (string.IsNullOrWhiteSpace(accidentNo))
            return;

        var groupName = $"{IncidentGroupPrefix}{accidentNo.Trim()}";
        await Clients.Group(groupName).SendAsync("ReceiveKpiUpdate", new
        {
            AccidentNo = accidentNo,
            Kpi = kpiSummary,
            UpdatedAt = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Broadcast a single patient's updated details (movement history, triage, etc).
    /// </summary>
    /// <param name="accidentNo">The incident number</param>
    /// <param name="caseNumber">The case/patient identifier</param>
    /// <param name="patientDetail">Patient detail object (movements, current status, etc)</param>
    /// <returns>Task representing the async operation</returns>
    public async Task BroadcastPatientDetail(string accidentNo, string caseNumber, object patientDetail)
    {
        if (string.IsNullOrWhiteSpace(accidentNo) || string.IsNullOrWhiteSpace(caseNumber))
            return;

        var groupName = $"{IncidentGroupPrefix}{accidentNo.Trim()}";
        await Clients.Group(groupName).SendAsync("ReceivePatientDetail", new
        {
            AccidentNo = accidentNo,
            CaseNumber = caseNumber,
            Detail = patientDetail,
            UpdatedAt = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Notify all connected clients of a critical system event (e.g., sync success/failure, connection lost).
    /// </summary>
    /// <param name="eventType">e.g., "SyncCompleted", "SyncFailed", "ConnectionLost"</param>
    /// <param name="message">Event message</param>
    /// <returns>Task representing the async operation</returns>
    public async Task BroadcastSystemEvent(string eventType, string message)
    {
        if (string.IsNullOrWhiteSpace(eventType))
            return;

        await Clients.All.SendAsync("ReceiveSystemEvent", new
        {
            EventType = eventType,
            Message = message,
            Timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Connection override: log when a client connects.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        await Clients.All.SendAsync("ReceiveSystemEvent", new
        {
            EventType = "ClientConnected",
            ConnectionId = Context.ConnectionId,
            Timestamp = DateTime.UtcNow
        });

        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Connection override: log when a client disconnects.
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await Clients.All.SendAsync("ReceiveSystemEvent", new
        {
            EventType = "ClientDisconnected",
            ConnectionId = Context.ConnectionId,
            Exception = exception?.Message,
            Timestamp = DateTime.UtcNow
        });

        await base.OnDisconnectedAsync(exception);
    }
}
