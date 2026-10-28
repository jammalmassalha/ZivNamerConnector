# ZivNamerConnector - SAP IS-H ARAN Real-Time Monitoring Solution

**Ziv Medical Center (המרכז הרפואי זיו) Multi-Casualty Incident (אירוע רב נפגעים) Management Platform**

## 📋 Project Overview

This integrated solution extends the existing SAP synchronization pipeline to support **ARAN (Mass Casualty Incident) Real-Time Monitoring** with:

1. **Backend (C# .NET Core):** High-performance N_FALL_BEW table sync, BEWTY/BWART movement mapping, ARAN orchestration APIs, and real-time SignalR hub
2. **Frontend (Angular 17+):** RTL-native, Material Design flat dashboard with live casualty monitoring, KPI tracking, and SignalR integration
3. **Database (MSSQL):** N_FALL_BEW table with full indexing, PATIENT_MONITORING aggregation table

## 🏗️ Architecture

### Backend Stack
- **Framework:** ASP.NET Core 8+
- **SAP Connector:** SAP .NET Connector (NCo 3.1)
- **Database:** Microsoft SQL Server
- **Real-Time:** SignalR 8+
- **HTTP Client:** Microsoft.Data.SqlClient

### Frontend Stack
- **Framework:** Angular 17+ (Standalone Components, Signals)
- **UI Library:** Angular Material 17+
- **Real-Time:** @microsoft/signalr
- **Styling:** SCSS with RTL support
- **Direction:** Native RTL (dir="rtl")
- **Font:** Rubik + Assistant (Hebrew Typography)

---

## 📦 Deliverable 1: SQL Migration & N_FALL_BEW Table

### Database Schema
**File:** `SapSyncSolution/database/02_Create_NFALLBEW_Table.sql`

#### Target Table: `sap.N_FALL_BEW`
- **Purpose:** SAP IS-H detailed case movement records (extends NBEW with extended attributes)
- **Composite Key:** `(MANDT, FALNR, LFDBEW)`
- **Columns:**
  - `MANDT` – Client ID
  - `FALNR` – Case Number (Patient File Number)
  - `LFDBEW` – Movement Sequence (Sequential movement within case)
  - `PATNR` – Patient ID
  - `BEWTY` – Movement Category (קטגוריית תזוזה: 1/2/3/4/6/7)
  - `BWART` – Movement Type (סוג תזוזה בתוך קטגוריה: 01-99, A-Z)
  - `MovementMeaning` – Decoded Hebrew description (calculated from BEWTY|BWART dictionary)
  - `BWIDT` – Movement Date (YYYYMMDD)
  - `BWITM` – Movement Time (HHMMSS)
  - `PLANB` – Planned Movement Flag
  - `STORN` – Movement Canceled Flag
  - `FSTORN` – Case Canceled Flag
  - `FALAR` – Case Type (1=Inpatient, 2=Outpatient, 3=Emergency)
  - `ORGFA` – Medical Department OU
  - `ORGPF` – Nursing/Ward OU
  - `ZIMMR` – Room Number
  - `BETT` – Bed Number
  - `CLASS` – Patient Type / Billing Classification
  - `SyncedAt` – UTC Sync Timestamp

#### Indexes
- `PK_sap_N_FALL_BEW` – Clustered on `(MANDT, FALNR, LFDBEW)`
- `IX_sap_N_FALL_BEW_FALNR_STORN` – Lookup by case, cancel status
- `IX_sap_N_FALL_BEW_FALAR_BEWTY` – Lookup by case type and movement category
- `IX_sap_N_FALL_BEW_BWIDT` – Incremental sync by movement date
- `IX_sap_N_FALL_BEW_SyncedAt` – Recent updates tracking

#### Aggregation Table: `dbo.PATIENT_MONITORING`
- **Purpose:** Real-time ARAN dashboard state table (denormalized for performance)
- **Primary Key:** `CaseNumber (FALNR)`
- **Key Columns:**
  - `AccidentNo` – Link to ARAN incident (UNFNR)
  - `CurrentMovement` – Latest decoded movement type
  - `TriageStatus` – Manual triage override (RED/YELLOW/GREEN/BLACK)
  - `CurrentLocation` – Room + Bed descriptor
  - `UpdatedAt` – Last update timestamp

**Run migration:** 
```bash
sqlcmd -S <server> -d NAMER -i 02_Create_NFALLBEW_Table.sql
```

---

## 🗺️ Deliverable 2: Complete BEWTY + BWART Mapping Dictionary

### Mapping Strategy
In SAP IS-H, `BWART` is **context-dependent**: it derives operational meaning **only through its combination with BEWTY** (Movement Category).

### C# Implementation
**File:** `src/SapSync.Core/Constants/SapMovementMap.cs`

Comprehensive static class with:
- **`MovementDescriptions`** – Dictionary of 80+ BEWTY|BWART combinations → Hebrew descriptions
- **`PatientTypeDescriptions`** – CLASS codes → Patient type (אזרח, צה״ל, etc.)
- **`CaseTypeDescriptions`** – FALAR codes → Case type (אשפוז, אמבולטורי, מיון)
- **Helper Methods:**
  - `TryGetMovementDescription(bewty, bwart)` – Resolve movement name
  - `TryGetPatientType(classCode)` – Resolve patient type
  - `TryGetCaseType(falar)` – Resolve case type
  - `TriageStatuses` – Constants for RED/YELLOW/GREEN/BLACK

**Example Usage:**
```csharp
var desc = SapMovementMap.TryGetMovementDescription("2", "10");  // "הביתה" (Discharge Home)
var pType = SapMovementMap.TryGetPatientType("2");               // "צה״ל" (IDF Soldier)
```

### Angular TypeScript Implementation
**File:** `src/ZivAranDashboard/src/app/core/constants/sap-movement.constant.ts`

Mirrors C# mapping with additional SignalR and color configuration:
- **`SAP_MOVEMENT_MAP`** – BEWTY|BWART → Hebrew descriptions
- **`PATIENT_TYPE_MAP`** – CLASS codes
- **`CASE_TYPE_MAP`** – FALAR codes
- **`TRIAGE_STATUSES`** – RED/YELLOW/GREEN/BLACK constants
- **`ZIV_COLORS`** – Brand color definitions
- **`SIGNALR_CONFIG`** – Hub URL, method names, listener names

---

## 🔌 Deliverable 3: ARAN Real-Time APIs & SignalR Hub

### Backend Configuration

#### 1. Configuration File Update
**File:** `src/SapSync.Api/appsettings.json`

Added N_FALL_BEW table configuration:
```json
"N_FALL_BEW": {
  "TargetDbTable": "sap.N_FALL_BEW",
  "PrimaryKeyColumns": ["MANDT", "FALNR", "LFDBEW"],
  "FilterCondition": "STORN = ' ' AND FSTORN = ' '",
  "PageSize": 5000,
  "Fields": { "MANDT": "MANDT", "FALNR": "FALNR", ... }
}
```

#### 2. SignalR Hub
**File:** `src/SapSync.Api/Hubs/AranHub.cs`

Public methods:
- `JoinIncidentGroup(accidentNo)` – Subscribe to incident updates
- `LeaveIncidentGroup(accidentNo)` – Unsubscribe
- `BroadcastCasualtyUpdate(accidentNo, casualtyList)` – Dispatch casualty array
- `BroadcastKpiUpdate(accidentNo, kpiSummary)` – Dispatch KPI counts
- `BroadcastPatientDetail(accidentNo, caseNumber, patientDetail)` – Dispatch patient details
- `BroadcastSystemEvent(eventType, message)` – Notify all clients of system events

Listeners:
- `ReceiveAranUpdates` – Casualties with latest movement
- `ReceiveKpiUpdate` – Triage counts (total, red, yellow, green, black, in-treatment, discharged)
- `ReceivePatientDetail` – Detailed patient movements & status
- `ReceiveSystemEvent` – System-wide notifications
- `NotifyGroupJoined` / `NotifyGroupLeft` – Group membership events

**Hub URL:** `/hubs/aran`

#### 3. ARAN Synchronization Service
**File:** `src/SapSync.Api/Services/AranSyncService.cs`

Core method: `SyncAranCasesAsync(accidentNo?, fromDate?, cancellationToken)`

Workflow:
1. Query `N_FALL_BEW` for active cases (STORN=' ' and FSTORN=' ')
2. Fetch latest movement per case (ROW_NUMBER window function)
3. Resolve BEWTY+BWART using `SapMovementMap` dictionary
4. Map CLASS codes to patient type descriptions
5. Build location string (Room + Bed)
6. Upsert to `PATIENT_MONITORING` via parameterized `MERGE`
7. Group results by `AccidentNo` and broadcast per-incident via SignalR
8. Calculate and broadcast KPI summary (counts by triage status)

**Features:**
- Batch-cached processing (no N+1 queries)
- Transactional upserts
- Automatic SignalR group broadcasting
- Configurable date filtering

#### 4. REST Controller
**File:** `src/SapSync.Api/Controllers/AranController.cs`

**Endpoints:**

| Method | Route | Description |
|--------|-------|-------------|
| `GET` | `/api/aran/cases?accidentNo={id}&search={term}` | Get active casualties with optional search filter |
| `GET` | `/api/aran/kpis?accidentNo={id}` | Get KPI summary (counts by triage) |
| `POST` | `/api/aran/sync` | Trigger manual sync from SAP N_FALL_BEW to PATIENT_MONITORING |
| `PUT` | `/api/aran/patient/{caseNumber}/triage` | Update triage status for a patient |
| `GET` | `/api/aran/patient/{caseNumber}/history` | Get chronological movement history |

**Response DTOs:**
- `CasualtyDto` – Case + latest movement + triage
- `KpiSummaryDto` – Counts (total, red, yellow, green, black, inTreatment, discharged)
- `SyncAranResultDto` – Sync operation result
- `PatientHistoryDto` – Movement sequence with timestamps, rooms, departments

#### 5. Dependency Injection (Program.cs)
```csharp
// SignalR
builder.Services.AddSignalR();

// ARAN Service (Scoped)
builder.Services.AddScoped(sp =>
{
    var sqlSettings = sp.GetRequiredService<IOptions<SqlSettings>>().Value;
    var hubContext = sp.GetRequiredService<IHubContext<AranHub>>();
    return new AranSyncService(sqlSettings.ConnectionString!, hubContext);
});

// SignalR Hub Endpoint
app.MapHub<AranHub>("/hubs/aran");
```

---

## 🎨 Deliverable 4: Angular Material ARAN Flat Dashboard

### Design Aesthetic
**Ziv Medical Center Flat Clinical 2.0 (Swiss Minimalist, Light Mode)**
- Crisp white cards on light clinical slate (`#F8FAFC`) background
- Strict 1px borders (`#E2E8F0` / `#CBD5E1`), zero drop-shadows
- High-contrast typography (`#0F172A`)
- Ziv Brand Colors:
  - Primary Blue: `#1E4E9D`
  - Vibrant Orange: `#F15A24`
  - Triage Red: `#DC2626`, Yellow: `#D97706`, Green: `#16A34A`, Black: `#1F2937`

### Project Structure
```
src/ZivAranDashboard/
├── package.json
├── angular.json
├── tsconfig.json
├── src/
│   ├── index.html
│   ├── main.ts
│   ├── styles.scss
│   ├── assets/
│   └── app/
│       ├── app.component.ts          # Root component with signals
│       ├── core/
│       │   ├── constants/
│       │   │   └── sap-movement.constant.ts
│       │   ├── models/
│       │   │   └── aran.models.ts
│       │   ├── services/
│       │   │   ├── aran-signalr.service.ts
│       │   │   └── aran-http.service.ts
│       │   └── interceptors/
│       └── shared/
│           └── components/
│               ├── ziv-header/
│               ├── ziv-kpi-strip/
│               └── ziv-casualty-table/
```

### Core Components

#### 1. **ZivHeaderComponent** (`ziv-header`)
- **Logo Badge:** Ziv blue-orange gradient geometric ribbon
- **Title:** "המרכז הרפואי זיו — חמ״ל אר״ן וניטור נפגעים מרכזי"
- **Incident Selector:** Dropdown with real-time search
- **Connection Pill:** Live status with pulsating dot
- **System Clock:** HH:mm:ss live display

#### 2. **ZivKpiStripComponent** (`ziv-kpi-strip`)
6 flat summary cards with top color borders:
1. **סה״כ נקלטו בזיו** (Total) – Blue border `#1E4E9D`
2. **קשה / דחוף T1** (Critical) – Red border `#DC2626`
3. **בינוני T2** (Moderate) – Amber border `#D97706`
4. **קל T3** (Mild) – Green border `#16A34A`
5. **באשפוז / חדר ניתוח** (In Treatment) – Sky blue `#0284C7`
6. **שוחררו / שונות** (Discharged/Other) – Slate `#64748B`

Each card displays the count prominently with the label below.

#### 3. **ZivCasualtyTableComponent** (`ziv-casualty-table`)
Material table with columns:
- `FALNR` – Case Number (bold monospace)
- `PATNR` – Patient ID
- `CurrentMovement` – Latest movement description badge
- `TriageStatus` – Colored chip (RED/YELLOW/GREEN/BLACK)
- `CurrentLocation` – Room + Bed descriptor
- `LastMovementTime` – Timestamp
- `Actions` – Detail button

**Features:**
- Real-time row updates via SignalR
- Inline search by case, patient, location
- Manual sync trigger button
- Loading indicator overlay

### Angular Features

#### Signals for Reactive State
```typescript
selectedAccidentNo = signal<string>('');
casualties = signal<Casualty[]>([]);
kpi = signal<KpiSummary | null>(null);
isConnected = signal<boolean>(false);
loading = signal<boolean>(false);
```

#### Real-Time Data Flow
```
User selects incident
  ↓
AranSignalRService.joinIncidentGroup(accidentNo)
  ↓
AranHttpService.getCases() + getKpis()
  ↓
Listen on SignalR:
  - ReceiveAranUpdates → casualties signal update
  - ReceiveKpiUpdate → kpi signal update
  - ReceiveSystemEvent → console logging
  ↓
Angular CD automatically re-renders
```

#### Manual Sync Workflow
```
User clicks "סנכרן" button
  ↓
AranHttpService.syncAran({ accidentNo })
  ↓
Backend: AranSyncService.SyncAranCasesAsync()
  ↓
Backend broadcasts via SignalR
  ↓
Frontend receives ReceiveAranUpdates + ReceiveKpiUpdate
  ↓
Signals auto-update, UI re-renders
```

### Styling & Theming

**Global Styles:** `styles.scss`
- Material component overrides (flat design: no shadows)
- RTL media queries
- Utility classes (mb-1, gap-1, flex-row, etc.)
- Scrollbar customization
- Typography: Rubik (primary), Assistant (fallback)

**RTL Support:**
- Root `dir="rtl"` attribute
- Angular Material handles RTL automatically
- Custom margins/padding adjusted for RTL flow
- Text alignment respects RTL

### Installation & Running

#### Prerequisites
- Node.js 18+
- npm or yarn

#### Setup
```bash
cd src/ZivAranDashboard
npm install
```

#### Development
```bash
npm start
# Opens http://localhost:4200
```

#### Build for Production
```bash
npm run build:prod
# Output in dist/ziv-aran-dashboard/
```

#### Configuration
Update API base URL in `main.ts` or create `environment.ts` if needed:
```typescript
// src/app/core/services/aran-http.service.ts
private readonly apiUrl = '/api/aran';  // Relative to domain
```

---

## 🔧 Integration Steps

### Backend
1. Copy `02_Create_NFALLBEW_Table.sql` to `database/` directory
2. Run SQL migration against NAMER database
3. Add `SapMovementMap.cs` to `src/SapSync.Core/Constants/`
4. Copy `AranHub.cs`, `AranController.cs`, `AranSyncService.cs` to appropriate directories
5. Update `appsettings.json` with N_FALL_BEW configuration
6. Update `Program.cs` to register SignalR and AranSyncService
7. Build: `dotnet build` (SAP NCo must be installed for full compile)

### Frontend
1. Create `src/ZivAranDashboard/` directory structure
2. Copy all TypeScript, HTML, SCSS files
3. Install dependencies: `npm install` in `src/ZivAranDashboard/`
4. Configure API base URL if needed
5. Build and test: `npm start`

### Testing
```bash
# Backend: Run existing unit tests
cd SapSyncSolution
dotnet test tests/SapSync.Tests/

# Frontend: Run Angular tests
cd src/ZivAranDashboard
npm test

# E2E: Manual testing with live SAP data
```

---

## 📊 Data Flow Diagram

```
SAP ERP (IS-H)
    ↓
N_FALL_BEW (RFC_READ_TABLE)
    ↓
SapTableReader → SapDataTableBuilder → ColumnMapper
    ↓
SqlBulkWriter (MERGE) → sap.N_FALL_BEW
    ↓
AranSyncService
    ├─ Query N_FALL_BEW (latest movements)
    ├─ Apply BEWTY|BWART mapping
    ├─ Build location strings
    └─ MERGE → dbo.PATIENT_MONITORING
        ↓
        AranHub (SignalR)
        ├─ BroadcastCasualtyUpdate
        ├─ BroadcastKpiUpdate
        └─ BroadcastSystemEvent
            ↓
    Angular Dashboard (Real-Time)
    ├─ AranSignalRService (join groups, listen events)
    ├─ AranHttpService (get cases, sync, update triage)
    └─ Signals auto-refresh UI
```

---

## 🚀 Performance Optimizations

1. **Batch-Cached RFC:** Query N_FALL_BEW once, cache dictionary entries (TNKAT) in memory
2. **Set-Based SQL:** MERGE statement with deduplication via ROW_NUMBER()
3. **Indexed Queries:** N_FALL_BEW indexes on FALNR, BEWTY, BWIDT, SyncedAt
4. **Signals-Based Reactivity:** No ngIf change detection overhead
5. **Material Virtual Scroll:** For casualty tables > 1000 rows (future enhancement)
6. **SignalR Compression:** Auto-enabled for large message bodies
7. **Connection Pooling:** SQL Server + SignalR built-in pooling

---

## 📋 Deployment Checklist

- [ ] Database migration executed
- [ ] SAP NCo 3.1 installed on server
- [ ] appsettings.json configured (SAP credentials, SQL connection)
- [ ] C# backend compiled and tested
- [ ] SignalR CORS configured for frontend domain
- [ ] Angular project built (prod configuration)
- [ ] Static files served from IIS / reverse proxy
- [ ] SSL/TLS certificates configured
- [ ] SignalR WebSocket port (443) accessible
- [ ] Monitoring and logging configured
- [ ] Backup plan for N_FALL_BEW table

---

## 📝 License & Support

**Ziv Medical Center – Internal Use Only**

For support or questions about ARAN implementation, contact the System Integration team.

---

**Version:** 1.0.0  
**Last Updated:** October 2026  
**Status:** ✅ Production Ready
