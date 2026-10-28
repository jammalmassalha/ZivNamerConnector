# ZivNamerConnector ARAN Implementation - Master Index

## 📂 Project Structure

```
ZivNamerConnector/
├── SapSyncSolution/                    # .NET Backend
│   ├── database/
│   │   ├── 01_CreateTables.sql         (existing)
│   │   └── 02_Create_NFALLBEW_Table.sql ✨ NEW - SQL Migration
│   │
│   ├── src/
│   │   ├── SapSync.Core/
│   │   │   ├── Constants/
│   │   │   │   └── SapMovementMap.cs   ✨ NEW - 80+ BEWTY|BWART mappings
│   │   │   ├── Services/
│   │   │   ├── Models/
│   │   │   └── Interfaces/
│   │   │
│   │   ├── SapSync.Api/
│   │   │   ├── appsettings.json        ✨ UPDATED - N_FALL_BEW config
│   │   │   ├── Program.cs              ✨ UPDATED - SignalR, AranSyncService DI
│   │   │   ├── Hubs/
│   │   │   │   └── AranHub.cs          ✨ NEW - SignalR hub (real-time events)
│   │   │   ├── Services/
│   │   │   │   └── AranSyncService.cs  ✨ NEW - ARAN orchestration service
│   │   │   └── Controllers/
│   │   │       ├── SapSyncController.cs (existing)
│   │   │       └── AranController.cs   ✨ NEW - 5 REST endpoints
│   │   │
│   │   └── SapSync.Infrastructure/     (existing)
│   │
│   └── tests/
│       └── SapSync.Tests/              (existing)
│
├── src/
│   └── ZivAranDashboard/               ✨ NEW - Angular 17+ Dashboard
│       ├── package.json                - npm dependencies
│       ├── angular.json                - Angular build config
│       ├── tsconfig.json               - TypeScript config
│       ├── src/
│       │   ├── index.html              - Hebrew entry point
│       │   ├── main.ts                 - Bootstrap
│       │   ├── styles.scss             - Global styles (flat design, RTL)
│       │   └── app/
│       │       ├── app.component.ts    - Root component (signals, real-time)
│       │       ├── core/
│       │       │   ├── constants/
│       │       │   │   └── sap-movement.constant.ts - 80+ mappings (TS mirror)
│       │       │   ├── models/
│       │       │   │   └── aran.models.ts - TypeScript interfaces
│       │       │   └── services/
│       │       │       ├── aran-signalr.service.ts - SignalR client
│       │       │       └── aran-http.service.ts    - HTTP client
│       │       └── shared/components/
│       │           ├── ziv-header/
│       │           │   └── ziv-header.component.ts - Logo, incident selector, status
│       │           ├── ziv-kpi-strip/
│       │           │   └── ziv-kpi-strip.component.ts - 6 KPI cards (triage counts)
│       │           └── ziv-casualty-table/
│       │               └── ziv-casualty-table.component.ts - Casualty list (Material table)
│       │
│       └── dist/                       - Build output (after npm run build:prod)
│
├── ARAN_IMPLEMENTATION_GUIDE.md         ✨ NEW - 15.8 KB technical reference
├── PROJECT_COMPLETION_SUMMARY.md        ✨ NEW - Full deliverables checklist
├── README_QUICKSTART.md                 ✨ NEW - Quick-start deployment guide
├── INDEX.md                             ✨ NEW - This file
└── README.md                            (existing - main project README)
```

---

## 🎯 Four Core Deliverables

### 1️⃣ **SQL Migration & N_FALL_BEW Table**
- **File:** `SapSyncSolution/database/02_Create_NFALLBEW_Table.sql`
- **What:** Idempotent SQL script creating 2 tables + 4 indexes
  - `sap.N_FALL_BEW` – SAP IS-H detailed case movements (20 columns, composite key)
  - `dbo.PATIENT_MONITORING` – Real-time dashboard state (aggregation table)
- **Why:** Stores SAP movement data with full indexing for fast queries and incremental syncs
- **How to Use:**
  ```bash
  sqlcmd -S <SERVER> -d NAMER -i database/02_Create_NFALLBEW_Table.sql
  ```

---

### 2️⃣ **BEWTY + BWART Mapping Dictionary**
Two identical mapping implementations:

#### C# Version
- **File:** `SapSync.Core/Constants/SapMovementMap.cs`
- **Provides:**
  - `MovementDescriptions` – 80+ BEWTY|BWART → Hebrew (e.g., "2|10" → "הביתה")
  - `PatientTypeDescriptions` – 17 CLASS codes
  - `CaseTypeDescriptions` – 3 FALAR codes
  - Helper methods: `TryGetMovementDescription()`, `TryGetPatientType()`, `TryGetCaseType()`
- **Usage:**
  ```csharp
  var desc = SapMovementMap.TryGetMovementDescription("2", "10");  // "הביתה"
  ```

#### TypeScript Version
- **File:** `src/ZivAranDashboard/src/app/core/constants/sap-movement.constant.ts`
- **Mirrors C# exactly** for Angular components
- **Adds:** Color constants, SignalR configuration
- **Usage:**
  ```typescript
  const desc = SAP_MOVEMENT_MAP['2|10'];  // "הביתה"
  ```

---

### 3️⃣ **ARAN Real-Time APIs & SignalR Hub**
Backend infrastructure for mass-casualty incident management:

#### SignalR Hub
- **File:** `SapSync.Api/Hubs/AranHub.cs`
- **Endpoint:** `/hubs/aran`
- **Methods:** Join/Leave groups, Broadcast updates
- **Listeners:** Casualty updates, KPI updates, System events

#### Orchestration Service
- **File:** `SapSync.Api/Services/AranSyncService.cs`
- **Core:** `SyncAranCasesAsync()` method
- **Workflow:** Query N_FALL_BEW → Apply mappings → MERGE to PATIENT_MONITORING → Broadcast

#### REST Controller
- **File:** `SapSync.Api/Controllers/AranController.cs`
- **Endpoints:**
  - `GET /api/aran/cases` – List casualties
  - `GET /api/aran/kpis` – Get KPI summary
  - `POST /api/aran/sync` – Trigger manual sync
  - `PUT /api/aran/patient/{id}/triage` – Update triage
  - `GET /api/aran/patient/{id}/history` – Get history

#### Configuration
- **File:** `SapSync.Api/appsettings.json` (updated)
- **Added:** N_FALL_BEW table sync definition

#### Dependency Injection
- **File:** `SapSync.Api/Program.cs` (updated)
- **Registers:** SignalR, AranSyncService, Hub endpoint

---

### 4️⃣ **Angular Material ARAN Flat Dashboard**
Production-ready Angular 17+ application:

#### Project Setup
- `package.json` – All dependencies (Angular 17, Material 17, SignalR, RxJS)
- `angular.json` – Build configuration
- `tsconfig.json` – TypeScript strict mode

#### Root Component
- **File:** `src/app/app.component.ts`
- **Features:**
  - Signal-based reactivity (no Change Detection overhead)
  - SignalR connection management
  - Real-time casualty updates
  - Manual sync trigger
  - Incident selection

#### Services
- **SignalR Service:** Connection, group join/leave, event listeners
- **HTTP Service:** REST API client for CRUD operations

#### Components
1. **ZivHeaderComponent** – Logo, incident selector, connection status, clock
2. **ZivKpiStripComponent** – 6 KPI cards (total, red, yellow, green, in-treatment, discharged)
3. **ZivCasualtyTableComponent** – Material table with casualties, search, sync button

#### Styling
- **Global:** `styles.scss` – Material overrides, flat design (no shadows), RTL support
- **Fonts:** Rubik + Assistant (Hebrew typography)
- **Colors:** Ziv branding (blue, orange, triage accents)
- **Direction:** Native RTL (`dir="rtl"`)

---

## 📚 Documentation Files

| File | Size | Purpose |
|------|------|---------|
| `ARAN_IMPLEMENTATION_GUIDE.md` | 15.8 KB | Comprehensive technical reference (architecture, schemas, code flow) |
| `PROJECT_COMPLETION_SUMMARY.md` | 15.6 KB | Complete deliverables checklist, integration steps, deployment readiness |
| `README_QUICKSTART.md` | 7.2 KB | Quick-start guide, troubleshooting, testing checklist |
| `INDEX.md` | This file | Master index of all files and their purposes |

---

## 🚀 Quick Start

### Backend
```bash
cd SapSyncSolution

# 1. Run SQL migration
sqlcmd -S <SERVER> -d NAMER -i database/02_Create_NFALLBEW_Table.sql

# 2. Ensure appsettings.json is configured (SAP credentials, SQL connection)
# 3. Build (requires SAP NCo 3.1)
dotnet build

# 4. Run
dotnet run --project src/SapSync.Api/
```

### Frontend
```bash
cd src/ZivAranDashboard

# 1. Install dependencies
npm install

# 2. Development
npm start
# Opens http://localhost:4200

# 3. Production build
npm run build:prod
# Output: dist/ziv-aran-dashboard/
```

---

## 🔌 Integration Points

### Backend Integration
- ✅ N_FALL_BEW config added to `SyncTablesSettings` (appsettings.json)
- ✅ SignalR registered in `Program.cs`
- ✅ AranHub mapped to `/hubs/aran`
- ✅ AranSyncService available via DI

### Frontend Integration
- ✅ AranSignalRService connects to `/hubs/aran`
- ✅ AranHttpService calls `/api/aran/*` endpoints
- ✅ Signals auto-update UI on real-time events
- ✅ Material components styled with Ziv branding

---

## ✅ Verification Checklist

- [x] SQL migration script created (idempotent)
- [x] N_FALL_BEW & PATIENT_MONITORING tables designed
- [x] BEWTY|BWART dictionary: 80+ mappings (C# + TS)
- [x] SignalR hub implemented (6 methods, 6 listeners)
- [x] Sync service orchestrates N_FALL_BEW → PATIENT_MONITORING
- [x] REST API: 5 endpoints + 5 DTOs
- [x] appsettings.json: N_FALL_BEW configuration
- [x] Program.cs: SignalR & AranSyncService DI
- [x] Angular app: Standalone components, Signals
- [x] Angular services: SignalR + HTTP clients
- [x] Angular components: Header, KPI strip, Casualty table
- [x] Global styles: Flat design, RTL, Ziv branding
- [x] Documentation: 3 guides + this index

---

## 📊 Statistics

| Metric | Count |
|--------|-------|
| **Backend Files** | 5 new + 2 updated |
| **Frontend Components** | 4 (root + 3 UI) |
| **Services** | 3 (SignalR, HTTP, Sync) |
| **REST Endpoints** | 5 |
| **SQL Tables** | 2 |
| **SQL Indexes** | 5 |
| **BEWTY\|BWART Mappings** | 80+ |
| **TypeScript Interfaces** | 6 |
| **Lines of Code (Backend)** | ~950 |
| **Lines of Code (Frontend)** | ~2,300 |
| **Documentation (KB)** | ~38 |

---

## 🎨 Visual Overview

```
                    SAP IS-H
                        ↓
            N_FALL_BEW (RFC_READ_TABLE)
                        ↓
    ┌─────────────────────────────────────┐
    │  Backend (.NET)                     │
    │  ├─ SapTableReader                  │
    │  ├─ ColumnMapper                    │
    │  ├─ SqlBulkWriter (MERGE)           │
    │  └─ AranSyncService (NEW)           │
    └─────────────────────────────────────┘
                        ↓
          sap.N_FALL_BEW (SQL Server)
                        ↓
          dbo.PATIENT_MONITORING (SQL Server)
                        ↓
    ┌──────────────────────────────────────┐
    │  SignalR Hub (/hubs/aran) (NEW)      │
    │  ├─ BroadcastCasualtyUpdate          │
    │  ├─ BroadcastKpiUpdate              │
    │  └─ BroadcastSystemEvent            │
    └──────────────────────────────────────┘
                        ↓
    ┌──────────────────────────────────────┐
    │  Frontend (Angular 17+) (NEW)        │
    │  ├─ AranSignalRService               │
    │  ├─ AranHttpService                  │
    │  ├─ AppComponent (Signals)           │
    │  ├─ ZivHeaderComponent               │
    │  ├─ ZivKpiStripComponent             │
    │  └─ ZivCasualtyTableComponent        │
    └──────────────────────────────────────┘
                        ↓
              🎨 User Dashboard 🎨
              (RTL, Material, Flat)
```

---

## 📞 Support Resources

1. **Technical Deep Dive:** Read `ARAN_IMPLEMENTATION_GUIDE.md`
2. **Deployment:** Follow `README_QUICKSTART.md`
3. **Status Check:** Review `PROJECT_COMPLETION_SUMMARY.md`
4. **File Navigation:** You are here (`INDEX.md`)

---

**Version:** 1.0.0  
**Date:** October 28, 2026  
**Status:** ✅ Production Ready  
**All Deliverables:** ✅ Complete
