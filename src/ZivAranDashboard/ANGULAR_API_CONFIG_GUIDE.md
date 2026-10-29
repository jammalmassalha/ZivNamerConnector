# Angular API URL Configuration Guide

## Overview

The Angular ZivAranDashboard application communicates with the backend API at `http://pbi_srv:546/api/`. This guide explains how to update the API URL for different environments.

## Configuration Locations

### 1. **Environment Files** (Recommended)

The API URL is now managed through environment configuration files:

#### Development Environment
**File:** `src/environments/environment.ts`

```typescript
export const environment = {
  production: false,
  apiUrl: 'http://localhost:5000/api'  // ← Change this for dev
};
```

**When used:** 
- Running `ng serve` (development mode)
- Local development and testing

#### Production Environment
**File:** `src/environments/environment.prod.ts`

```typescript
export const environment = {
  production: true,
  apiUrl: 'http://pbi_srv:546/api'  // ← Change this for production
};
```

**When used:**
- Running `ng build --configuration=production`
- Published to IIS or production server

---

## How to Update API URL

### Option 1: Update Environment Files (Recommended)

1. **For Development:**
   ```bash
   # Edit src/environments/environment.ts
   # Change: apiUrl: 'http://localhost:5000/api'
   #     to: apiUrl: 'http://your-dev-server:port/api'
   ```

2. **For Production:**
   ```bash
   # Edit src/environments/environment.prod.ts
   # Change: apiUrl: 'http://pbi_srv:546/api'
   #     to: apiUrl: 'http://your-prod-server:port/api'
   ```

### Option 2: Runtime Configuration (via `appsettings.json`)

If you want to configure the API URL at runtime (without rebuilding), you can add it to `appsettings.json` in the backend API project and inject it:

**In your backend `appsettings.json`:**
```json
{
  "ApiUrl": "http://pbi_srv:546/api"
}
```

Then fetch it in Angular's initialization.

---

## Build & Deploy

### Development
```bash
# Uses environment.ts with development apiUrl
ng serve
# App runs on http://localhost:4200
# API calls go to: http://localhost:5000/api
```

### Production
```bash
# Uses environment.prod.ts with production apiUrl
ng build --configuration=production
# Builds to dist/ folder
# API calls go to: http://pbi_srv:546/api
```

### Deploy to IIS
```bash
# 1. Build production bundle
ng build --configuration=production

# 2. Copy dist/ziv-aran-dashboard/* to C:\inetpub\wwwroot\aran\

# 3. Configure IIS Application:
#    - Physical path: C:\inetpub\wwwroot\aran
#    - Binding: http://localhost:80 or your domain

# 4. App will use environment.prod.ts (http://pbi_srv:546/api)
```

---

## API Service Usage

The `AranHttpService` now uses the environment configuration:

**File:** `src/app/core/services/aran-http.service.ts`

```typescript
import { environment } from '../../../environments/environment';

@Injectable({ providedIn: 'root' })
export class AranHttpService {
  private readonly apiUrl = `${environment.apiUrl}/aran`;

  constructor(private http: HttpClient) {}

  getCases(accidentNo: string): Observable<Casualty[]> {
    return this.http.get<Casualty[]>(
      `${this.apiUrl}/cases?accidentNo=${accidentNo}`
    );
  }
}
```

When the app runs:
- **Dev mode:** Uses `environment.ts` → `http://localhost:5000/api/aran/...`
- **Prod mode:** Uses `environment.prod.ts` → `http://pbi_srv:546/api/aran/...`

---

## Testing API Endpoints

### Verify API is Responding
```bash
# Test if the API endpoint is reachable
curl -i http://pbi_srv:546/api/aran/cases?accidentNo=123

# Expected: 200 OK with JSON response
```

### Check Angular App Configuration
```bash
# Open browser console (F12)
# In DevTools Console:
localStorage.getItem('apiUrl')  // May not show if using environment
```

### Debug API Calls
```typescript
// Add to component.ts for debugging
constructor(private aran: AranHttpService) {
  console.log('API URL:', (window as any).environment?.apiUrl);
}
```

---

## Common Scenarios

### Scenario 1: Local Development
**Change:** `src/environments/environment.ts`
```typescript
apiUrl: 'http://localhost:5000/api'
```
**Command:** `ng serve`

### Scenario 2: Staging Server
**Change:** `src/environments/environment.ts`
```typescript
apiUrl: 'http://staging-server:5000/api'
```
**Command:** `ng serve`

### Scenario 3: Production on pbi_srv
**Already configured in:** `src/environments/environment.prod.ts`
```typescript
apiUrl: 'http://pbi_srv:546/api'
```
**Command:** `ng build --configuration=production`

### Scenario 4: Different Port
**Change:** `src/environments/environment.prod.ts`
```typescript
// From:
apiUrl: 'http://pbi_srv:546/api'

// To (e.g., port 8080):
apiUrl: 'http://pbi_srv:8080/api'
```

---

## Troubleshooting

### Issue: API calls to wrong URL
- **Solution:** Check `src/environments/environment.ts` or `environment.prod.ts`
- **Debug:** Run `ng build --configuration=production --verbose`

### Issue: CORS errors when calling API
- **Cause:** Browser security policy blocking cross-origin requests
- **Solution:** Backend API must enable CORS for Angular app domain
  ```csharp
  // In Program.cs of backend API
  builder.Services.AddCors(options =>
  {
    options.AddPolicy("AllowAngular", builder =>
      builder.WithOrigins("http://localhost:4200", "http://pbi_srv")
             .AllowAnyMethod()
             .AllowAnyHeader()
    );
  });
  app.UseCors("AllowAngular");
  ```

### Issue: Mixed Content (HTTP/HTTPS)
- **Cause:** Angular app on HTTPS trying to call HTTP API
- **Solution:** Use HTTPS for both app and API, or configure proxy

### Issue: API returns 404
- **Cause:** Wrong API URL or endpoint path
- **Debug Steps:**
  1. Check `environment.ts` or `environment.prod.ts`
  2. Verify API endpoint exists: `curl http://pbi_srv:546/api/aran/...`
  3. Check browser Network tab in DevTools

---

## File Structure

```
src/
├── environments/
│   ├── environment.ts          ← Development (localhost:5000)
│   └── environment.prod.ts     ← Production (pbi_srv:546)
├── app/
│   ├── core/
│   │   └── services/
│   │       └── aran-http.service.ts  ← Uses environment config
│   └── ...
└── ...
```

---

## Quick Reference

| Environment | File | API URL | When Used |
|---|---|---|---|
| **Development** | `environment.ts` | `http://localhost:5000/api` | `ng serve` |
| **Production** | `environment.prod.ts` | `http://pbi_srv:546/api` | `ng build --configuration=production` |

---

## Related Files

- **Backend API Config:** `SapSyncSolution/src/SapSync.Api/appsettings.json`
- **Backend API Port:** Configured in `launchSettings.json` (currently port 5001)
- **IIS Binding:** Configured in IIS Manager or `web.config`

---

**Last Updated:** 2026-10-29
**Angular Version:** 18+
**TypeScript Version:** 5.2+
