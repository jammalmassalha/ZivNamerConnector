# Angular ARAN Dashboard - Deployment Verification Checklist

## Pre-Deployment Verification

### 1. Build Verification

```bash
cd src/ZivAranDashboard

# Test build with sub-path
ng build --configuration production --base-href=/pbi_srv/zivarantest/

# Verify build output
ls -la dist/ziv-aran-dashboard/
```

**Expected Results:**
- ✅ Build completes with 0 errors
- ✅ `dist/ziv-aran-dashboard/index.html` exists
- ✅ `dist/ziv-aran-dashboard/styles.*.css` exists
- ✅ `dist/ziv-aran-dashboard/main.*.js` exists
- ✅ Total size < 2MB

### 2. Base Href Verification

```bash
# Check base href is correctly set
grep 'base href' dist/ziv-aran-dashboard/index.html
```

**Expected Output:**
```html
<base href="/pbi_srv/zivarantest/">
```

### 3. Material Icons Verification

```bash
# Check Google Fonts link is present
grep 'Material.*Icons' dist/ziv-aran-dashboard/index.html
grep 'Material.*Symbols' dist/ziv-aran-dashboard/index.html
```

**Expected:**
- ✅ `Material+Symbols+Outlined` in href
- ✅ `Material+Icons` in href
- ✅ `display=swap` parameter present

---

## IIS Deployment Steps

### Step 1: Prepare IIS Physical Path

```powershell
# Run as Administrator
$AppPath = "C:\inetpub\wwwroot\zivarantest"

# Create directory
New-Item -ItemType Directory -Path $AppPath -Force

# Copy built files
Copy-Item ".\dist\ziv-aran-dashboard\*" -Destination $AppPath -Recurse -Force

# Verify files copied
Get-ChildItem -Path $AppPath | Measure-Object
```

**Expected:** All files from `dist/ziv-aran-dashboard/` copied to IIS path

### Step 2: Create IIS Application Pool

```powershell
# Create app pool
New-WebAppPool -Name "ZivAran" -Force
Set-ItemProperty IIS:\AppPools\ZivAran -Name "processModel.identityType" -Value "ApplicationPoolIdentity"

# Verify
Get-WebAppPool -Name "ZivAran"
```

### Step 3: Create IIS Application

**Via PowerShell:**
```powershell
New-WebApplication -Name "pbi_srv/zivarantest" -Site "Default Web Site" -PhysicalPath "C:\inetpub\wwwroot\zivarantest" -ApplicationPool "ZivAran" -Force
```

**Or via IIS Manager:**
1. Open IIS Manager
2. Expand "Sites" → "Default Web Site"
3. Right-click → "Add Application"
4. **Alias:** `pbi_srv/zivarantest` (match your path)
5. **Application pool:** `ZivAran`
6. **Physical path:** `C:\inetpub\wwwroot\zivarantest`
7. Click OK

### Step 4: Configure web.config

**Create `C:\inetpub\wwwroot\zivarantest\web.config`:**

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <system.webServer>
    <handlers>
      <clear />
      <add name="StaticFile" path="*" verb="*" modules="StaticFileModule" resourceType="Unspecified" requireAccess="Read" />
    </handlers>

    <staticContent>
      <clientCache cacheControlMode="UseMaxAge" cacheControlMaxAge="31536000" />
      <mimeMap fileExtension=".json" mimeType="application/json" />
      <mimeMap fileExtension=".woff" mimeType="application/font-woff" />
      <mimeMap fileExtension=".woff2" mimeType="application/font-woff2" />
    </staticContent>

    <httpCompression>
      <dynamicTypes>
        <add mimeType="application/javascript" enabled="true" />
        <add mimeType="text/css" enabled="true" />
        <add mimeType="application/json" enabled="true" />
      </dynamicTypes>
    </httpCompression>

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

    <httpProtocol>
      <customHeaders>
        <add name="X-Content-Type-Options" value="nosniff" />
        <add name="X-Frame-Options" value="SAMEORIGIN" />
        <add name="X-XSS-Protection" value="1; mode=block" />
      </customHeaders>
    </httpProtocol>
  </system.webServer>
</configuration>
```

**Important:** Change `/pbi_srv/zivarantest/` to match your deployment path!

### Step 5: Set Folder Permissions

```powershell
$AppPath = "C:\inetpub\wwwroot\zivarantest"

# Grant read/execute to app pool identity
icacls $AppPath /grant "IIS AppPool\ZivAran:(OI)(CI)(R,RX)" /T

# Grant modify if you need uploads/logs
icacls "$AppPath\logs" /grant "IIS AppPool\ZivAran:(OI)(CI)(M)" /T -ErrorAction SilentlyContinue
```

---

## Post-Deployment Verification

### Test 1: Application Loads

**URL:** `http://localhost/pbi_srv/zivarantest/`

**Expected Results:**
```
✅ Page loads without 404
✅ No JavaScript console errors (F12 → Console)
✅ Ziv Medical Center logo visible
✅ Title shows "המרכז הרפואי זיו - אר״ן מעקב"
✅ Background is light gray (#f8fafc)
```

**If fails:**
- Check IIS Logs: `%SystemRoot%\System32\LogFiles\HTTPERR\httperr.log`
- Check Event Viewer → Application
- Verify `web.config` syntax is valid
- Verify physical path exists and permissions are set

### Test 2: Material Icons Render

**In Browser Console (F12):**
```javascript
// Check icon font loaded
const icons = document.querySelectorAll('mat-icon, .material-icons');
icons.length > 0 ? "✓ Icons found" : "✗ No icons"

// Check font is loaded
fetch('/pbi_srv/zivarantest/assets/fonts/...').then(r => r.status)
```

**Expected Results:**
```
✅ Wifi icon shows as icon (not "wifi" text)
✅ Sync icon shows as icon (not "sync" text)
✅ Search icon shows as icon (not "search" text)
✅ All icons have ltr direction (no RTL flip)
```

**If fails:**
- Check Network tab for 404s on fonts.googleapis.com
- Verify Material Icons stylesheet loaded
- Check browser cache (Ctrl+Shift+Del)
- Verify Google Fonts not blocked by firewall

### Test 3: API URLs Are Correct

**Open DevTools → Network tab, then interact with app:**

```
✅ GET /pbi_srv/zivarantest/api/aran/cases?accidentNo=XXX
✅ GET /pbi_srv/zivarantest/api/aran/kpis?accidentNo=XXX
✅ POST /pbi_srv/zivarantest/api/aran/sync

❌ NOT /api/aran/cases (missing sub-path)
❌ NOT /api/aran/kpis (missing sub-path)
```

**If API calls use wrong paths:**
- Verify `environment.prod.ts` has dynamic URL resolution
- Rebuild with `--base-href=/pbi_srv/zivarantest/`
- Check `<base href>` tag in index.html
- Clear browser cache

### Test 4: Form Layout & Hebrew Text

**In Browser:**
```
✅ Incident dropdown ("בחר אירוע") fully visible
✅ Search field ("חיפוש") not clipped
✅ Sync button ("סנכרן") not overlapping
✅ All Hebrew text right-aligned (RTL)
✅ No overlapping or squashed elements
```

**If layout issues:**
- Open DevTools → Elements
- Check for CSS `overflow: hidden` in wrong places
- Verify `.mat-form-field { direction: rtl; }`
- Check for hardcoded fixed widths limiting flex items

### Test 5: Data Loading

**Steps:**
1. Select an incident from dropdown (e.g., "INC001")
2. Wait 2-3 seconds for data to load

**Expected Results:**
```
✅ Casualty table populates with rows
✅ KPI summary shows counts (Red, Yellow, Green, Black)
✅ No "אין נפגעים פעילים" (no active casualties) message
✅ Last update timestamp shows
✅ Loading spinner displays then disappears
```

**If data doesn't load:**
- Check Network tab for 404 on `/api/aran/cases`
- Verify API server is running and responding
- Check API CORS headers if cross-origin
- Look for error message in browser console
- Verify incident number exists in database

### Test 6: Polling Service Status

**In Browser Console (F12):**
```javascript
// Check polling is active
document.querySelector('[class*="status-indicator"]')?.textContent
// Should show: "Live Polling" or similar

// Check poll count increases
setInterval(() => {
  const pollText = document.querySelector('[class*="status-details"]')?.textContent;
  console.log(pollText);
}, 5000);
```

**Expected Results:**
```
✅ Status indicator shows "Live Polling" (green pulsing dot)
✅ Poll counter increments every 60 seconds
✅ Last poll timestamp updates
✅ No errors in console related to polling
```

### Test 7: Network Performance

**Open DevTools → Network tab, then reload:**

**Expected Results:**
```
✅ index.html: < 50 KB
✅ styles.*.css: < 100 KB
✅ main.*.js: < 500 KB
✅ Total page size: < 1 MB
✅ Fonts load from googleapis.com (cached after first visit)
✅ All assets load successfully (0 404 errors)
```

**If slow loading:**
- Enable gzip compression in IIS
- Enable static file caching in `web.config`
- Minimize bundle size: `ng build --configuration production --aot`

### Test 8: Browser Compatibility

**Test on multiple browsers:**

| Browser | Status | Notes |
|---------|--------|-------|
| Chrome | ✅ | Material Design works perfectly |
| Edge | ✅ | Chromium-based, same as Chrome |
| Firefox | ✅ | May need font-display: swap |
| Safari | ✅ | iOS needs zoom-fix for Material |

```javascript
// Test WebSocket support (if using SignalR)
'WebSocket' in window ? "✓ WebSocket supported" : "✗ WebSocket not supported"
```

### Test 9: CORS & API Integration

**If API on different domain, verify CORS headers:**

```powershell
# Check CORS headers from API
$headers = (Invoke-WebRequest "http://pbi_srv:546/api/aran/cases?accidentNo=TEST").Headers
$headers['Access-Control-Allow-Origin']
$headers['Access-Control-Allow-Methods']
```

**Expected:**
```
Access-Control-Allow-Origin: *
Access-Control-Allow-Methods: GET, POST, PUT, DELETE, OPTIONS
Access-Control-Allow-Headers: Content-Type, Authorization
```

### Test 10: RTL & Hebrew Localization

**Verify text direction and alignment:**

```javascript
// Check HTML direction
document.documentElement.dir // Should be "rtl"

// Check Material components have RTL support
document.querySelector('.mat-form-field')?.dir // Should be "rtl"
document.querySelector('.mat-select')?.dir // Should be "rtl"
```

**Expected:**
```
✅ All text right-aligned
✅ Form inputs aligned right
✅ Dropdown arrows on left side
✅ Icons not flipped (icons have direction: ltr)
✅ Hebrew text readable and properly spaced
```

---

## Common Issues & Fixes

### Issue: 404 on API Calls

**Symptom:** Network tab shows `/api/aran/cases` returning 404

**Fix:**
```bash
# Rebuild with correct base-href
ng build --configuration production --base-href=/pbi_srv/zivarantest/

# Verify index.html
grep 'base href' dist/ziv-aran-dashboard/index.html
```

### Issue: Icons Show as Raw Text

**Symptom:** Wifi icon shows as "wifi_off", Sync shows as "sync"

**Fix:**
1. Clear browser cache: `Ctrl+Shift+Del`
2. Check `index.html` has Material Icons stylesheet
3. Verify fonts.googleapis.com not blocked
4. Check Network tab for 403 on fonts

### Issue: Forms Clipped/Overlapping

**Symptom:** Dropdown and search squeezed to corner

**Fix:**
- Check `ziv-header.component.ts` uses flex layout
- Verify no hardcoded `max-width` constraints
- Check `width: 100%` on flex items

### Issue: Blank Page or 404

**Symptom:** `/pbi_srv/zivarantest/` shows 404 or blank

**Fix:**
```powershell
# Verify web.config URL rewrite rule
[xml]$config = Get-Content C:\inetpub\wwwroot\zivarantest\web.config
$config.configuration.'system.webServer'.rewrite.rules.rule | Select-Object -Property name, patternSyntax

# Verify physical path
Get-Item C:\inetpub\wwwroot\zivarantest\index.html
```

### Issue: Data Not Loading ("אין נפגעים פעילים")

**Symptom:** Incident selected but table empty

**Fix:**
1. Check API is running: `curl http://pbi_srv:546/api/aran/cases?accidentNo=TEST`
2. Check incident exists in database
3. Check Network tab for `/api/aran/cases` response
4. Look for error message in browser console
5. Verify API CORS is enabled

---

## Performance Optimization

### Enable Gzip Compression

```xml
<!-- web.config -->
<httpCompression>
  <dynamicTypes>
    <add mimeType="application/javascript" enabled="true" />
    <add mimeType="text/css" enabled="true" />
    <add mimeType="application/json" enabled="true" />
  </dynamicTypes>
</httpCompression>
```

### Enable Static Asset Caching

```xml
<staticContent>
  <clientCache cacheControlMode="UseMaxAge" cacheControlMaxAge="31536000" />
</staticContent>
```

### Monitor Performance

```powershell
# Check response times
(Invoke-WebRequest "http://localhost/pbi_srv/zivarantest/").StatusCode
(Invoke-WebRequest "http://localhost/pbi_srv/zivarantest/api/aran/cases?accidentNo=TEST").StatusCode
```

---

## Rollback Plan

If deployment fails:

```powershell
# Stop IIS Application
Stop-WebApplication -Name "Default Web Site/pbi_srv/zivarantest"

# Remove current version
Remove-Item -Path "C:\inetpub\wwwroot\zivarantest\*" -Recurse -Force

# Restore from backup (if available)
Copy-Item -Path "C:\backup\zivarantest\*" -Destination "C:\inetpub\wwwroot\zivarantest\" -Recurse -Force

# Start IIS Application
Start-WebApplication -Name "Default Web Site/pbi_srv/zivarantest"
```

---

## Documentation References

- **Deployment Guide:** `DEPLOYMENT_SUBPATH_GUIDE.md`
- **Polling Service:** `POLLING_SERVICE_GUIDE.md`
- **API Configuration:** `ANGULAR_API_CONFIG_GUIDE.md`
- **IIS Setup:** `../SapSyncSolution/IIS_DEPLOYMENT_GUIDE.md`
- **Build Scripts:** `build-for-iis.sh` or `build-for-iis.ps1`

---

**Last Updated:** 2026-10-29
**Status:** Ready for verification
**Tested with:** Angular 18+, IIS 10.0+, Windows Server 2019+
