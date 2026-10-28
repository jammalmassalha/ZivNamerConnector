# ARAN Implementation - Deployment Checklist

## ✅ Pre-Deployment Verification

### 1. Code Compilation
- [ ] Backend compiles cleanly
  ```bash
  cd SapSyncSolution
  dotnet build
  ```
  **Note:** SAP NCo 3.1 (x86) must be installed for full compilation. If not available, install from SAP Support Portal.

- [ ] Frontend dependencies resolve
  ```bash
  cd src/ZivAranDashboard
  npm install
  ```

### 2. Database Migration
- [ ] SQL Server instance accessible
  ```bash
  sqlcmd -S <SERVER> -d NAMER -E
  ```

- [ ] Migration script runs without errors
  ```bash
  sqlcmd -S <SERVER> -d NAMER -i SapSyncSolution/database/02_Create_NFALLBEW_Table.sql
  ```

- [ ] Verify tables created
  ```sql
  SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME IN ('N_FALL_BEW', 'PATIENT_MONITORING')
  ```

- [ ] Verify indexes created
  ```sql
  SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.N_FALL_BEW')
  ```

### 3. Configuration Verification
- [ ] **appsettings.json** configured:
  - [ ] SAP RFC destination credentials
  - [ ] SQL Server connection string
  - [ ] CORS AllowedOrigins includes frontend URL
  - [ ] SignalR enabled

- [ ] **Frontend environment** configured:
  - [ ] Backend API URL in services
  - [ ] SignalR hub URL correct

### 4. Dependency Verification
- [ ] **.NET Dependencies:**
  - [ ] Microsoft.AspNetCore.SignalR
  - [ ] Microsoft.Data.SqlClient
  - [ ] SAP .NET Connector 3.1 (optional, for live SAP)

- [ ] **Node.js Dependencies:**
  - [ ] @angular/core 17+
  - [ ] @angular/material 17+
  - [ ] @microsoft/signalr
  - [ ] rxjs

## 🧪 Functional Testing

### 1. Backend API Endpoints
- [ ] **GET /api/aran/cases**
  ```bash
  curl -X GET "http://localhost:5000/api/aran/cases?accidentNo=TEST001"
  ```

- [ ] **GET /api/aran/kpis**
  ```bash
  curl -X GET "http://localhost:5000/api/aran/kpis?accidentNo=TEST001"
  ```

- [ ] **POST /api/aran/sync**
  ```bash
  curl -X POST "http://localhost:5000/api/aran/sync" \
    -H "Content-Type: application/json" \
    -d '{"accidentNo":"TEST001","limit":100}'
  ```

- [ ] **PUT /api/aran/patient/{id}/triage**
  ```bash
  curl -X PUT "http://localhost:5000/api/aran/patient/12345/triage" \
    -H "Content-Type: application/json" \
    -d '{"triageStatus":"RED","notes":"Critical condition"}'
  ```

- [ ] **GET /api/aran/patient/{id}/history**
  ```bash
  curl -X GET "http://localhost:5000/api/aran/patient/12345/history"
  ```

### 2. SignalR Connection
- [ ] Hub accessible at `/hubs/aran`
  ```bash
  # Monitor WebSocket in browser DevTools
  # Network tab should show ws://localhost:5000/hubs/aran connection
  ```

- [ ] Groups join/leave work
  - [ ] Join: `connection.invoke("JoinIncidentGroup", "TEST001")`
  - [ ] Leave: `connection.invoke("LeaveIncidentGroup", "TEST001")`

- [ ] Broadcasts received
  - [ ] Listen for `ReceiveAranUpdates`
  - [ ] Listen for `ReceiveKpiUpdate`
  - [ ] Listen for `ReceiveSystemEvent`

### 3. Angular Dashboard
- [ ] Frontend builds without errors
  ```bash
  cd src/ZivAranDashboard
  npm run build
  ```

- [ ] Development server starts
  ```bash
  npm start
  # Should open http://localhost:4200
  ```

- [ ] UI loads correctly
  - [ ] Header visible (Ziv logo, incident selector)
  - [ ] KPI strip visible (6 cards)
  - [ ] Casualty table visible
  - [ ] No console errors

- [ ] Real-time updates work
  - [ ] Click "סנכרן" button
  - [ ] Table updates in real-time
  - [ ] KPI counts update

- [ ] Incident selection works
  - [ ] Select different incident from dropdown
  - [ ] Table filters correctly
  - [ ] No errors in console

### 4. Data Integration
- [ ] N_FALL_BEW table populated
  ```sql
  SELECT COUNT(*) FROM sap.N_FALL_BEW
  ```

- [ ] PATIENT_MONITORING table populated
  ```sql
  SELECT COUNT(*) FROM dbo.PATIENT_MONITORING
  ```

- [ ] BEWTY|BWART mappings applied correctly
  ```sql
  SELECT DISTINCT BEWTY, BWART, MovementMeaning 
  FROM dbo.PATIENT_MONITORING 
  ORDER BY BEWTY, BWART
  ```

## 🔐 Security Checklist

- [ ] SQL injection protection
  - [ ] All queries use parameterized statements
  - [ ] No string concatenation in SQL

- [ ] CORS configured restrictively
  - [ ] AllowedOrigins limited to known domains
  - [ ] No wildcard "*" in production

- [ ] SignalR security
  - [ ] Hub requires authentication (if applicable)
  - [ ] Groups isolation enforced
  - [ ] No sensitive data in messages

- [ ] Environment secrets protected
  - [ ] appsettings.json not committed to git
  - [ ] Use environment variables or Key Vault
  - [ ] SAP credentials encrypted

## 📊 Performance Testing

- [ ] Backend response time < 1 second
  ```bash
  time curl http://localhost:5000/api/aran/cases
  ```

- [ ] SignalR latency acceptable
  - [ ] Monitor message delivery time
  - [ ] Check WebSocket compression enabled

- [ ] Database queries optimized
  ```sql
  -- Check index usage
  SET STATISTICS IO ON
  SELECT * FROM dbo.PATIENT_MONITORING WHERE FALAR = '1'
  SET STATISTICS IO OFF
  ```

- [ ] Angular build size acceptable
  ```bash
  cd src/ZivAranDashboard
  npm run build:prod -- --stats-json
  # Check dist/ folder size
  ```

## 📝 Documentation Verification

- [ ] README_QUICKSTART.md reviewed
- [ ] ARAN_IMPLEMENTATION_GUIDE.md reviewed
- [ ] PROJECT_COMPLETION_SUMMARY.md reviewed
- [ ] INDEX.md reviewed
- [ ] Code comments present for complex logic
- [ ] TypeScript interfaces documented
- [ ] API endpoints documented

## 🚀 Deployment Steps

### 1. Backend Deployment
```bash
# Build release
cd SapSyncSolution
dotnet publish -c Release -o ../build/

# Deploy to server (IIS, Docker, etc.)
# Configure appsettings.production.json
# Run database migration
# Start service
```

### 2. Frontend Deployment
```bash
# Build production
cd src/ZivAranDashboard
npm run build:prod

# Deploy dist/ to CDN or web server
# Configure API endpoints for production
# Test from production URL
```

### 3. Post-Deployment Verification
- [ ] Backend API reachable
- [ ] Frontend loads from CDN/web server
- [ ] SignalR connection established
- [ ] Real-time updates work end-to-end
- [ ] No console errors
- [ ] Database accessible
- [ ] SAP RFC connectivity functional (if applicable)

## ✨ Sign-Off

- [ ] All tests passed
- [ ] Security review completed
- [ ] Performance acceptable
- [ ] Documentation reviewed
- [ ] Team sign-off obtained
- [ ] Ready for production

---

**Deployment Date:** _______________  
**Deployed By:** _______________  
**Verified By:** _______________  
**Notes:** _______________

---

**Version:** 1.0.0  
**Last Updated:** October 28, 2026
