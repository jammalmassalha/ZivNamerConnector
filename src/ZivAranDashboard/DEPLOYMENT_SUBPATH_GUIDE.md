# Angular Deployment to IIS Sub-Path Guide

## Problem: App Deployed at `/pbi_srv/zivarantest/` with 404 Errors

When deploying the Angular ARAN dashboard to an IIS virtual directory like `/pbi_srv/zivarantest/`, the app must be built with the correct `--base-href` flag and configured to handle sub-path routing properly.

---

## Solution Overview

### 1. **Material Icons Fix** ✅
- Added Material Symbols + Material Icons font families to `index.html`
- Added comprehensive Material Icons CSS to `styles.scss`
- Ensures icons render as glyphs, not raw text ligatures

### 2. **Sub-Path URL Resolution** ✅
- Environment files now read `<base href>` dynamically from HTML
- API and Hub URLs automatically prepend the base path
- Works for any deployment path: `/`, `/pbi_srv/zivarantest/`, `/app/aran/`, etc.

### 3. **Form Layout Fixes** ✅
- Flexbox-based responsive design
- Proper RTL alignment for Hebrew text
- Prevents clipping and overlapping elements

---

## Build Instructions

### For Local Development (Localhost)

```bash
cd src/ZivAranDashboard

# Build without sub-path (default base-href = /)
ng build --configuration development

# Serve locally
ng serve --host 0.0.0.0 --port 4200
```

**Result:** App runs at `http://localhost:4200/`

### For IIS Deployment at `/pbi_srv/zivarantest/`

```bash
cd src/ZivAranDashboard

# Build with sub-path base-href
ng build --configuration production --base-href=/pbi_srv/zivarantest/

# Output: dist/ziv-aran-dashboard/
```

**Key:** The trailing slash `/` is critical!

### For Alternative IIS Paths

Replace `/pbi_srv/zivarantest/` with your actual path:

```bash
# Option 1: Different virtual directory
ng build --configuration production --base-href=/myapp/

# Option 2: Different domain with path
ng build --configuration production --base-href=/hospitalapps/aran/

# Option 3: Root domain (no path)
ng build --configuration production --base-href=/
```

---

## IIS Configuration

### Step 1: Create Application Pool

```powershell
# Run as Administrator
$appPoolName = "ZivAran"
$appPoolPath = "C:\inetpub\wwwroot\zivarantest"

# Create app pool
New-WebAppPool -Name $appPoolName -Force
Set-ItemProperty IIS:\AppPools\$appPoolName -Name "processModel.identityType" -Value "ApplicationPoolIdentity"
```

### Step 2: Create Virtual Directory & Deploy Files

```powershell
# Create physical directory
New-Item -ItemType Directory -Path $appPoolPath -Force

# Copy built files
Copy-Item ".\dist\ziv-aran-dashboard\*" -Destination $appPoolPath -Recurse -Force

# Set permissions
icacls $appPoolPath /grant "IIS AppPool\ZivAran:(OI)(CI)(R,RX)" /T
```

### Step 3: Create IIS Application

In **IIS Manager**:
1. Right-click "Default Web Site" → "Add Application"
2. **Alias:** `pbi_srv/zivarantest` (or your path)
3. **Application pool:** `ZivAran`
4. **Physical path:** `C:\inetpub\wwwroot\zivarantest`
5. Click OK

### Step 4: Configure web.config

Create `C:\inetpub\wwwroot\zivarantest\web.config`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <system.webServer>
    <!-- Enable compression -->
    <httpCompression>
      <dynamicTypes>
        <add mimeType="application/javascript" enabled="true" />
        <add mimeType="text/css" enabled="true" />
        <add mimeType="text/html" enabled="true" />
        <add mimeType="application/json" enabled="true" />
      </dynamicTypes>
    </httpCompression>

    <!-- Cache static assets -->
    <staticContent>
      <clientCache cacheControlMode="UseMaxAge" cacheControlMaxAge="31536000" />
      <mimeMap fileExtension=".json" mimeType="application/json" />
    </staticContent>

    <!-- URL Rewrite: Redirect to index.html for all routes -->
    <rewrite>
      <rules>
        <rule name="AngularJS Routes" stopProcessing="true">
          <match url=".*" />
          <conditions logicalGrouping="MatchAll">
            <add input="{REQUEST_FILENAME}" matchType="IsFile" negate="true" />
            <add input="{REQUEST_FILENAME}" matchType="IsDirectory" negate="true" />
            <add input="{REQUEST_URI}" pattern="^/(api|hubs|swagger)" negate="true" />
          </conditions>
          <action type="Rewrite" url="/pbi_srv/zivarantest/index.html" />
        </rule>
      </rules>
    </rewrite>

    <!-- CORS for API -->
    <httpProtocol>
      <customHeaders>
        <add name="Access-Control-Allow-Origin" value="*" />
        <add name="Access-Control-Allow-Methods" value="GET, POST, PUT, DELETE, OPTIONS" />
        <add name="Access-Control-Allow-Headers" value="Content-Type, Authorization" />
      </customHeaders>
    </httpProtocol>
  </system.webServer>
</configuration>
```

**Important:** In the rewrite rule, change `/pbi_srv/zivarantest/index.html` to match your deployment path!

### Step 5: Verify Deployment

1. Navigate to `http://localhost/pbi_srv/zivarantest/`
2. Confirm Material Icons render (not raw text ligatures)
3. Confirm forms are properly aligned (not clipped/overlapping)
4. Open browser DevTools → Network tab
5. Verify API calls go to `/pbi_srv/zivarantest/api/...` (not `/api/...`)

---

## How URL Resolution Works

### The Process

1. **Build Phase:**
   ```bash
   ng build --base-href=/pbi_srv/zivarantest/
   ```
   - Angular writes `<base href="/pbi_srv/zivarantest/">` to `dist/index.html`

2. **Runtime Phase (Browser):**
   ```typescript
   // In environment.ts
   function getBaseUrl(): string {
     const base = document.querySelector('base')?.getAttribute('href') || '/';
     return base.replace(/\/$/, ''); // "/pbi_srv/zivarantest"
   }
   ```
   - Angular reads the base tag from the HTML
   - Strips the trailing slash
   - Returns `/pbi_srv/zivarantest`

3. **API Calls:**
   ```typescript
   const apiUrl = getBaseUrl() + '/api'; // "/pbi_srv/zivarantest/api"
   const hubUrl = getBaseUrl() + '/hubs/aran'; // "/pbi_srv/zivarantest/hubs/aran"
   ```
   - All API calls are prefixed with the sub-path
   - HTTP Request to `http://pbi_srv:546/pbi_srv/zivarantest/api/aran/cases?accidentNo=INC001`

### Why This Works

- **Relative URLs:** The app uses relative paths (no `http://` or domain)
- **Browser Resolution:** Browser automatically prepends the domain
- **IIS Routing:** IIS knows the app is at `/pbi_srv/zivarantest/` and routes accordingly

---

## Troubleshooting

### Issue: 404 Errors on API Calls

**Symptom:** Network tab shows:
```
GET http://localhost/api/aran/cases → 404
(Should be: http://localhost/pbi_srv/zivarantest/api/aran/cases)
```

**Cause:** Angular built without `--base-href` or base tag is wrong

**Fix:**
```bash
# Rebuild with correct base-href
ng build --configuration production --base-href=/pbi_srv/zivarantest/
```

### Issue: Routing Not Working (Blank Page)

**Symptom:** Navigate to `/pbi_srv/zivarantest/incidents` → 404

**Cause:** IIS rewrite rule not configured

**Fix:** Add to `web.config`:
```xml
<rule name="AngularJS Routes" stopProcessing="true">
  <match url=".*" />
  <conditions logicalGrouping="MatchAll">
    <add input="{REQUEST_FILENAME}" matchType="IsFile" negate="true" />
    <add input="{REQUEST_FILENAME}" matchType="IsDirectory" negate="true" />
    <add input="{REQUEST_URI}" pattern="^/(api|hubs)" negate="true" />
  </conditions>
  <action type="Rewrite" url="/pbi_srv/zivarantest/index.html" />
</rule>
```

### Issue: Material Icons Not Rendering

**Symptom:** Icons show as `_off`, `menu`, `wifi` instead of actual icons

**Cause:** Google Fonts stylesheet blocked or slow to load

**Check:**
1. Open DevTools → Network tab
2. Search for `fonts.googleapis.com`
3. If 403/blocked, use local fonts (see below)

**Solution: Use Local Fonts**

1. Download `.woff2` files from Google Fonts
2. Place in `src/assets/fonts/`
3. Update `styles.scss`:

```scss
@font-face {
  font-family: 'Material Icons';
  src: url('/pbi_srv/zivarantest/assets/fonts/MaterialIcons-Regular.woff2') format('woff2');
}

@font-face {
  font-family: 'Material Symbols Outlined';
  src: url('/pbi_srv/zivarantest/assets/fonts/MaterialSymbolsOutlined[FILL,GRAD,opsz,wght].woff2') format('woff2');
}
```

### Issue: Forms Clipped/Overlapping

**Symptom:** Incident dropdown, search, and sync button squeezed to top-left

**Cause:** CSS Flexbox/Grid not properly configured

**Fix:** Check `ziv-header.component.ts` has proper flex layout:

```scss
.header-actions {
  display: flex;
  gap: 16px;
  align-items: center;
  flex: 1;
}

.incident-dropdown {
  flex: 1;
  min-width: 200px;
}

.search-field {
  flex: 1;
  min-width: 250px;
}

.sync-button {
  flex-shrink: 0;
}
```

### Issue: RTL Text Not Aligned

**Symptom:** Hebrew text appears LTR or misaligned

**Cause:** Missing `dir="rtl"` or CSS direction

**Fix:** Ensure in every component:
```typescript
template: `<div dir="rtl" class="component">...</div>`
```

And in `styles.scss`:
```scss
html, body {
  direction: rtl;
}

.mat-form-field {
  direction: rtl;
}

.mat-select {
  direction: rtl;
}
```

---

## Development vs Production URLs

| Environment | Base Href | API URL | Hub URL |
|---|---|---|---|
| Local Dev | `/` | `/api` | `/hubs/aran` |
| Staging IIS | `/staging/aran/` | `/staging/aran/api` | `/staging/aran/hubs/aran` |
| Production IIS | `/pbi_srv/zivarantest/` | `/pbi_srv/zivarantest/api` | `/pbi_srv/zivarantest/hubs/aran` |

---

## Performance & Caching

### Enable Caching for Static Assets

```xml
<!-- web.config -->
<staticContent>
  <clientCache cacheControlMode="UseMaxAge" cacheControlMaxAge="31536000" />
</staticContent>
```

### Enable Gzip Compression

```xml
<httpCompression>
  <dynamicTypes>
    <add mimeType="application/javascript" enabled="true" />
    <add mimeType="text/css" enabled="true" />
    <add mimeType="application/json" enabled="true" />
  </dynamicTypes>
</httpCompression>
```

---

## Files Changed

| File | Change |
|---|---|
| `index.html` | Added Material Fonts, proper base href comment |
| `styles.scss` | Added Material Icons CSS, RTL fixes, Flexbox utilities |
| `environment.ts` | Dynamic base URL resolution |
| `environment.prod.ts` | Dynamic base URL resolution |
| `aran-http.service.ts` | Console logging for debug |

---

## Deployment Checklist

- [ ] Build with correct `--base-href` flag
- [ ] Copy `dist/ziv-aran-dashboard/*` to IIS physical path
- [ ] Create IIS Application with virtual directory
- [ ] Configure `web.config` with URL rewrite rules
- [ ] Test Material Icons render properly
- [ ] Test form layout (no clipping)
- [ ] Test API calls in DevTools Network tab
- [ ] Test RTL Hebrew text alignment
- [ ] Clear browser cache (`Ctrl+Shift+Del`)
- [ ] Test on multiple browsers (Chrome, Edge, Firefox)
- [ ] Verify polling service works (status bar updates)
- [ ] Check IIS logs for 404 errors

---

## Related Documentation

- [Angular API Configuration](./ANGULAR_API_CONFIG_GUIDE.md)
- [Polling Service Guide](./POLLING_SERVICE_GUIDE.md)
- [IIS Deployment Guide](../SapSyncSolution/IIS_DEPLOYMENT_GUIDE.md)

---

**Last Updated:** 2026-10-29
**Status:** Ready for production deployment
**Tested with:** Angular 18+, IIS 10.0+
