# Angular ARAN Polling Service: Complete Implementation Guide

## Overview

The **AranPollingService** replaces WebSocket/SignalR with efficient HTTP-based background polling. It fetches casualty data every 60 seconds from `/api/aran/cases`, detects changes intelligently, and triggers audio/visual alerts without blocking the UI.

---

## 1. Architecture & Design

### Key Design Principles
- **Signal-Based Reactivity:** Uses Angular 16+ `signal()` for type-safe, automatic UI updates
- **Computed Signals:** Derived state (connectionStatus, hasActiveAlert) computed automatically
- **RxJS Timer:** `timer(0, 60000)` triggers immediate poll + 60-second intervals
- **Smart Delta Detection:** Compares casualty state by `caseNumber` to identify:
  - Brand new casualties (not in previous map)
  - Triage escalations (RED flag on status change)
  - Location/movement updates
- **Web Audio API Chime:** No external MP3 files; generates sine/triangle waves programmatically
- **Auto-Dismissing Alerts:** Toast notifications disappear after 6 seconds
- **Row Flash Highlight:** Updated case rows glow for 4 seconds

### Why Replace SignalR?
| Feature | SignalR | HTTP Polling |
|---------|---------|--------------|
| **Real-time** | Yes (WebSocket) | ~60s latency |
| **Requires Hub** | Yes (complex) | No (REST endpoint) |
| **Offline Mode** | No | Partial (cached data) |
| **IIS Sub-Path** | ❌ URL rewrite complex | ✅ Simple path concatenation |
| **Firewall Friendly** | WebSocket ports restricted | HTTP/HTTPS only |
| **CPU Load** | High (persistent connection) | Low (polling intervals) |
| **Implementation** | 500+ lines SignalR code | 280 lines polling + Web Audio |

---

## 2. Service API Reference

### Signals (Reactive State)

#### `readonly cases: Signal<Casualty[]>`
- **Purpose:** Current casualty list for the selected incident
- **Type:** `Casualty[]` array with `caseNumber`, `triageStatus`, `currentLocation`, etc.
- **Updated:** Every successful poll
- **Usage:**
  ```typescript
  constructor(private pollingService: AranPollingService) {}
  
  // In component
  casualtyList = computed(() => this.pollingService.cases());
  // or in template
  @for (case of pollingService.cases(); track case.caseNumber) {
    <tr>{{ case.caseNumber }}</tr>
  }
  ```

#### `readonly activeAlert: Signal<AranAlert | null>`
- **Purpose:** Current floating notification toast
- **Type:** `{ id, title, message, type: 'critical'|'new'|'update', timestamp }`
- **Lifecycle:** Auto-set for 6 seconds, then cleared
- **Usage:**
  ```typescript
  @if (pollingService.activeAlert(); as alert) {
    <div class="alert" [class.critical]="alert.type === 'critical'">
      {{ alert.title }}<br/>{{ alert.message }}
    </div>
  }
  ```

#### `readonly updatedCaseIds: Signal<Set<string>>`
- **Purpose:** Case IDs that changed in the last poll
- **Type:** `Set<string>` of caseNumbers
- **Lifecycle:** Populated on delta, cleared after 4 seconds
- **Usage:** Bind CSS class for row highlight:
  ```html
  <tr [ngClass]="{'row-highlight': pollingService.updatedCaseIds().has(case.caseNumber)}">
    {{ case.caseNumber }}
  </tr>
  ```
  ```scss
  .row-highlight {
    background-color: #fef3c7; // amber-100
    transition: background-color 1s ease-out;
  }
  ```

#### `readonly isSyncing: Signal<boolean>`
- **Purpose:** HTTP request in progress indicator
- **Type:** `boolean`
- **Updated:** Set to `true` when polling starts, `false` when response arrives
- **Usage:** Spinner or status bar:
  ```html
  @if (pollingService.isSyncing()) {
    <mat-spinner diameter="24"></mat-spinner>
  }
  ```

#### `readonly lastSyncTime: Signal<string>`
- **Purpose:** Last successful poll timestamp (Hebrew formatted)
- **Type:** `string` in format `HH:mm:ss`
- **Updated:** Every successful API response
- **Usage:** Display in status bar:
  ```html
  סנכרון אחרון: {{ pollingService.lastSyncTime() }}
  ```

#### `readonly isConnected: Signal<boolean>`
- **Purpose:** API connectivity status
- **Type:** `boolean`
- **Updated:** Set to `true` on successful poll, `false` on HTTP error
- **Usage:** Connection badge:
  ```html
  <span [class]="pollingService.isConnected() ? 'text-green-600' : 'text-red-600'">
    {{ pollingService.isConnected() ? '● מחובר' : '✕ מנותק' }}
  </span>
  ```

### Computed Signals

#### `readonly hasActiveAlert: Signal<boolean>`
- **Computed from:** `activeAlert() !== null`
- **Usage:** Show/hide alert container:
  ```html
  @if (pollingService.hasActiveAlert()) {
    <alert-toast [alert]="pollingService.activeAlert()!"></alert-toast>
  }
  ```

#### `readonly connectionStatus: Signal<string>`
- **Returns:**
  - `'סנכרון פעיל (כל 60 שנ׳)'` if `isSyncing()`
  - `'מחובר בזמן אמת'` if `isConnected()`
  - `'מנותק - לחץ לרענון'` otherwise
- **Usage:** Status display:
  ```html
  <span>{{ pollingService.connectionStatus() }}</span>
  ```

### Methods

#### `startPolling(): void`
**Starts the polling cycle.**
- Triggers immediate poll (0ms delay)
- Subsequent polls every 60,000ms (1 minute)
- Sets `isSyncing` during HTTP request
- Calls `processDelta()` on response
- Logs errors without throwing

**When to Call:**
- On component initialization (ngOnInit)
- When incident selected
- After manual refresh

**Example:**
```typescript
export class AranDashboardComponent implements OnInit {
  constructor(private pollingService: AranPollingService) {}

  ngOnInit() {
    this.pollingService.startPolling();
  }
}
```

#### `stopPolling(): void`
**Stops all polling activity.**
- Unsubscribes from timer
- Clears internal subscription
- Safe to call multiple times

**When to Call:**
- On component destroy (ngOnDestroy)
- When switching to different incident
- Before logging out

**Example:**
```typescript
ngOnDestroy() {
  this.pollingService.stopPolling();
}
```

#### `refreshNow(): void`
**Triggers an immediate poll cycle.**
- Does NOT interrupt normal 60s schedule
- Sets `isSyncing(true)` immediately
- Useful for manual "Sync Now" button

**When to Call:**
- User clicks refresh button
- Manual data refresh requested
- After critical action needing immediate verification

**Example:**
```html
<button (click)="pollingService.refreshNow()" 
        [disabled]="pollingService.isSyncing()">
  <mat-icon>refresh</mat-icon> רענן עכשיו
</button>
```

#### `private processDelta(newCases: Casualty[]): void`
**Internal: Compares old vs. new data and triggers alerts.**

**Logic:**
1. Build map of old cases by `caseNumber`
2. Iterate through new cases:
   - **New case?** → Increment `newVictimsCount`, add to `changedIds`
   - **Status changed?** → Increment `changedIds`
   - **Triage escalated to RED?** → Increment `criticalEscalations`
3. If changes found AND data not first load:
   - Call `triggerAlert(newVictimsCount, criticalEscalations, changedIds)`
4. Update `cases` signal with new data

**Skips alerts on:**
- Initial load (when `currentMap.size === 0`)
- No changes detected

**Example Change Detection:**
```
Previous: { caseNumber: "FAL-001", triageStatus: "YELLOW", currentLocation: "Waiting" }
New:      { caseNumber: "FAL-001", triageStatus: "RED",    currentLocation: "OR-2" }
         
         ✓ Triage changed (YELLOW → RED) = Critical Escalation
         ✓ Location changed (Waiting → OR-2) = Movement Alert
```

#### `private triggerAlert(...): void`
**Internal: Fires audio chime, shows toast, highlights rows.**

**Three-layer notification system:**

1. **Audio Chime** via `playHospitalChime()`:
   - Critical: 880Hz triangle wave → 440Hz (A5 → A4 sweep)
   - Standard: 587Hz sine wave → 880Hz (D5 → A5 sweep)
   - Duration: 300ms with exponential fade
   - Works offline, no external MP3 files
   - Gracefully degrades if AudioContext unavailable

2. **Row Highlight** via `updatedCaseIds`:
   - Affected case numbers added to signal
   - Auto-cleared after 4 seconds
   - CSS transition creates soft fade effect

3. **Toast Notification** via `activeAlert`:
   - **Critical:** `{ type: 'critical', title: 'התראת טריאז׳ קריטי!', message: '...' }`
   - **Standard:** `{ type: 'new', title: 'עדכון נפגעים בזמן אמת', message: '...' }`
   - Auto-dismissed after 6 seconds

---

## 3. Component Integration Examples

### Basic Usage in Component

```typescript
import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { AranPollingService } from './core/services/aran-polling.service';

@Component({
  selector: 'app-aran-dashboard',
  templateUrl: './aran-dashboard.component.html',
  styleUrls: ['./aran-dashboard.component.scss']
})
export class AranDashboardComponent implements OnInit, OnDestroy {
  private pollingService = inject(AranPollingService);

  // Expose signals to template
  readonly cases = this.pollingService.cases;
  readonly activeAlert = this.pollingService.activeAlert;
  readonly updatedCaseIds = this.pollingService.updatedCaseIds;
  readonly isSyncing = this.pollingService.isSyncing;
  readonly connectionStatus = this.pollingService.connectionStatus;

  ngOnInit() {
    // Start polling on component init
    this.pollingService.startPolling();
  }

  ngOnDestroy() {
    // Stop polling on component destroy
    this.pollingService.stopPolling();
  }

  onRefreshClick() {
    this.pollingService.refreshNow();
  }
}
```

### Template with Alerts & Row Highlighting

```html
<!-- Floating Alert Toast (Top Corner) -->
@if (activeAlert(); as alert) {
  <div class="fixed top-6 right-6 z-50 p-4 rounded-lg shadow-lg backdrop-blur-md"
       [ngClass]="alert.type === 'critical' 
                  ? 'bg-red-50 border-2 border-red-300' 
                  : 'bg-blue-50 border-2 border-blue-300'"
       dir="rtl">
    <div class="font-bold" 
         [class.text-red-700]="alert.type === 'critical'"
         [class.text-blue-700]="alert.type === 'new'">
      {{ alert.title }}
    </div>
    <div class="text-sm text-slate-600">{{ alert.message }}</div>
    <div class="text-xs text-slate-400 mt-1">{{ alert.timestamp }}</div>
  </div>
}

<!-- Status Bar -->
<div class="bg-slate-100 px-4 py-2 flex items-center justify-between">
  <span [class.text-green-600]="connectionStatus().includes('מחובר')"
        [class.text-red-600]="connectionStatus().includes('מנותק')">
    {{ connectionStatus() }}
  </span>
  <span class="text-sm text-slate-600">
    סנכרון אחרון: {{ lastSyncTime() }}
  </span>
  <button (click)="onRefreshClick()" 
          [disabled]="isSyncing()"
          class="px-3 py-1 bg-blue-600 text-white rounded hover:bg-blue-700 disabled:opacity-50">
    @if (isSyncing()) {
      <mat-spinner diameter="16" class="inline"></mat-spinner>
    } @else {
      <mat-icon>refresh</mat-icon>
    }
    רענן עכשיו
  </button>
</div>

<!-- Cases Table with Row Highlight -->
<table class="w-full">
  <thead>
    <tr>
      <th>מספר תיק</th>
      <th>סוג טריאז'</th>
      <th>מיקום</th>
      <th>עדכון אחרון</th>
    </tr>
  </thead>
  <tbody>
    @for (casualty of cases(); track casualty.caseNumber) {
      <tr [ngClass]="{
            'bg-amber-100 transition-colors duration-1000': updatedCaseIds().has(casualty.caseNumber),
            'hover:bg-slate-50': !updatedCaseIds().has(casualty.caseNumber)
          }">
        <td>{{ casualty.caseNumber }}</td>
        <td>
          <span [class]="getTriageClass(casualty.triageStatus)">
            {{ casualty.triageStatus }}
          </span>
        </td>
        <td>{{ casualty.currentLocation }}</td>
        <td>{{ casualty.updatedAt | date:'HH:mm:ss' }}</td>
      </tr>
    }
  </tbody>
</table>
```

### Component Methods for Triage Display

```typescript
getTriageClass(status?: string): string {
  switch (status) {
    case 'RED':
      return 'px-3 py-1 bg-red-100 text-red-700 rounded font-bold';
    case 'YELLOW':
      return 'px-3 py-1 bg-yellow-100 text-yellow-700 rounded font-bold';
    case 'GREEN':
      return 'px-3 py-1 bg-green-100 text-green-700 rounded font-bold';
    case 'BLACK':
      return 'px-3 py-1 bg-slate-200 text-slate-700 rounded font-bold';
    default:
      return 'px-3 py-1 bg-slate-100 text-slate-600 rounded';
  }
}
```

---

## 4. HTTP API Contract

### Polling Endpoint
```
GET {base}/api/aran/cases
```

**Headers:**
```
Authorization: Bearer {token}  // if required
Content-Type: application/json
```

**Response (200 OK):**
```json
[
  {
    "caseNumber": "FAL-001",
    "patientID": "PAT-123456",
    "accidentNo": "ACC-2026-10-29-001",
    "caseType": "INJURY",
    "currentMovement": "ADMITTED",
    "currentLocation": "OR-2",
    "triageStatus": "RED",
    "patientType": "CIVILIAN",
    "lastMovementTime": "2026-10-29T12:15:30Z",
    "updatedAt": "2026-10-29T12:20:45Z"
  },
  {
    "caseNumber": "FAL-002",
    "patientID": "PAT-123457",
    "accidentNo": "ACC-2026-10-29-001",
    "caseType": "INJURY",
    "currentMovement": "IN_TREATMENT",
    "currentLocation": "ICU-3",
    "triageStatus": "YELLOW",
    "patientType": "MILITARY",
    "lastMovementTime": "2026-10-29T12:10:15Z",
    "updatedAt": "2026-10-29T12:18:30Z"
  }
]
```

**Error Responses:**
| Status | Body | Handled By |
|--------|------|------------|
| 400 | `{ "error": "Invalid incident ID" }` | Caught by `catchError`, logged, `isConnected` → false |
| 401 | `{ "error": "Unauthorized" }` | Polling stops, redirect to login |
| 404 | `{ "error": "Incident not found" }` | No data, alert shown |
| 500 | `{ "error": "Server error" }` | Logged, retry on next 60s interval |

**Key Requirements:**
- Response must be `Casualty[]` array
- All timestamps in ISO 8601 format (with Z)
- `caseNumber` is unique identifier (used for delta detection)
- `triageStatus` must be `'RED' | 'YELLOW' | 'GREEN' | 'BLACK'` or undefined

---

## 5. IIS Sub-Path URL Resolution

The polling service automatically handles IIS virtual directories.

### How It Works

1. **HTML Base Tag** (in `index.html`):
   ```html
   <base href="/pbi_srv/zivarantest/">
   ```

2. **Polling Service Constructor**:
   ```typescript
   private getApiUrl(): string {
     const base = document.querySelector('base')
       ?.getAttribute('href')
       ?.replace(/\/$/, '') || '';
     return `${base}/api/aran/cases`;
   }
   ```

3. **Result**:
   - Reads `<base href>` at runtime
   - Strips trailing slash: `/pbi_srv/zivarantest/` → `/pbi_srv/zivarantest`
   - Appends `/api/aran/cases`
   - Final URL: `/pbi_srv/zivarantest/api/aran/cases`
   - Browser resolves relative to current origin

### For Different Deployments

| Deployment | Base Href | Final API URL |
|------------|-----------|---------------|
| Root path | `/` | `/api/aran/cases` |
| Sub-path (IIS) | `/pbi_srv/zivarantest/` | `/pbi_srv/zivarantest/api/aran/cases` |
| Docker/K8s | `/app/aran/` | `/app/aran/api/aran/cases` |

### Build Command for Deployment
```bash
ng build --configuration production --base-href /pbi_srv/zivarantest/
```

---

## 6. Web Audio API: Hospital Chime Implementation

### How It Works

```typescript
private playHospitalChime(isCritical: boolean): void {
  try {
    // 1. Create audio context
    const ctx = new (window.AudioContext || 
                     (window as any).webkitAudioContext)();
    
    // 2. Create oscillator (wave generator) + gain (volume)
    const osc = ctx.createOscillator();
    const gain = ctx.createGain();

    // 3. Configure wave based on severity
    if (isCritical) {
      // Critical: Triangle wave at high freq, steep sweep
      osc.type = 'triangle';
      osc.frequency.setValueAtTime(880, ctx.currentTime);      // A5
      osc.frequency.exponentialRampToValueAtTime(440, 
                                                 ctx.currentTime + 0.3); // A4
    } else {
      // Standard: Sine wave at medium freq, gentle sweep
      osc.type = 'sine';
      osc.frequency.setValueAtTime(587.33, ctx.currentTime);    // D5
      osc.frequency.exponentialRampToValueAtTime(880, 
                                                 ctx.currentTime + 0.3); // A5
    }

    // 4. Configure fade-out (20% → 1% over 300ms)
    gain.gain.setValueAtTime(0.2, ctx.currentTime);
    gain.gain.exponentialRampToValueAtTime(0.01, 
                                          ctx.currentTime + 0.4);

    // 5. Connect and play
    osc.connect(gain);
    gain.connect(ctx.destination);
    osc.start();
    osc.stop(ctx.currentTime + 0.4);

  } catch (e) {
    // Silent fail: autoplay policy blocks AudioContext until click
    console.log('[AranPollingService] AudioContext unavailable:', e);
  }
}
```

### Audio Parameters

| Attribute | Critical | Standard |
|-----------|----------|----------|
| Wave Type | Triangle (harsh) | Sine (mellow) |
| Start Freq | 880 Hz (A5, high) | 587.33 Hz (D5, medium) |
| End Freq | 440 Hz (A4, low) | 880 Hz (A5, high) |
| Duration | 300ms | 300ms |
| Fade | 0.2 → 0.01 (20x) | 0.2 → 0.01 (20x) |
| Effect | Descending chirp (alarm) | Ascending chirp (alert) |

### Browser Support
- ✅ Chrome / Edge / Brave (all versions)
- ✅ Firefox (all versions)
- ✅ Safari 14+
- ⚠️ Safari 13: Requires `webkitAudioContext` (handled via fallback)
- ❌ IE 11: Not supported, silently fails

### Autoplay Policy
Most browsers require user interaction before playing audio. The service handles this gracefully:
- First poll after user click: Audio plays
- Subsequent polls: Audio plays (gesture quota continues)
- Offline mode: No error (exception caught)

---

## 7. Performance Considerations

### Network Usage
- **Per Poll:** ~2-5 KB (JSON payload)
- **Per Hour:** 60 polls × 3 KB = ~180 KB
- **Per Day:** 180 KB × 24 = ~4.3 MB

### CPU & Memory
- **Polling Interval:** Low CPU (timer runs every 60s)
- **Delta Detection:** O(n) comparison (typically < 100 cases)
- **Memory:** ~100 KB per 1000 casualties in cache
- **No Memory Leaks:** Subscriptions unsubscribed on `ngOnDestroy`

### Optimization Tips
1. **Limit visible rows** on table (virtualScroll for 1000+ rows)
2. **Debounce alerts** (already 300ms debounce)
3. **Cache responses** with HTTP interceptor
4. **Compress API responses** (gzip on backend)
5. **Paginate results** if > 5000 casualties per incident

---

## 8. Debugging & Troubleshooting

### Enable Console Logging
All service methods log to console with `[AranPollingService]` prefix:

```
[AranPollingService] Initialized with API URL: /pbi_srv/zivarantest/api/aran/cases
[AranPollingService] Starting polling...
[AranPollingService] New casualty: FAL-001
[AranPollingService] Critical escalation: FAL-002 → RED
[AranPollingService] Played critical chime
```

### Common Issues

**Q: No data appears on load**
- A: Service hasn't started polling. Call `startPolling()` in `ngOnInit`.
- Check browser console for API URL and HTTP errors.

**Q: Audio chime doesn't play**
- A: Browser autoplay policy. Click somewhere on page first.
- Check DevTools console for `AudioContext` messages.
- Verify speakers/audio output working.

**Q: Alerts never appear**
- A: Check if `activeAlert` signal is rendering in template.
- Verify `activeAlert !== null` check with `@if`.
- Check component change detection (should be automatic with signals).

**Q: API calls 404**
- A: Base href mismatch. Verify `<base href>` in `index.html`.
- Backend must serve `/api/aran/cases` (no prefix needed if already under `/pbi_srv/zivarantest`).

**Q: Polls stop after first one**
- A: Unhandled exception in `processDelta`. Check browser console.
- Service catches errors and continues, but logs them.

### Debug Checklist
- [ ] `<base href>` matches deployment path
- [ ] `/api/aran/cases` responds with valid JSON
- [ ] Component calls `startPolling()` in `ngOnInit`
- [ ] Component calls `stopPolling()` in `ngOnDestroy`
- [ ] Template uses `@if` for signal-based conditionals
- [ ] Track function on `@for` uses unique identifier
- [ ] Backend returns `Casualty[]` with required fields

---

## 9. Migration from SignalR

### Steps to Remove SignalR

1. **Remove npm package:**
   ```bash
   npm uninstall @microsoft/signalr
   ```

2. **Delete SignalR service:**
   ```bash
   rm src/app/core/services/aran-signalr.service.ts
   ```

3. **Remove SignalR imports from components:**
   ```typescript
   // ❌ DELETE THIS
   import { AranSignalRService } from './aran-signalr.service';
   
   // ✅ USE THIS INSTEAD
   import { AranPollingService } from './aran-polling.service';
   ```

4. **Replace SignalR calls:**
   ```typescript
   // ❌ OLD
   constructor(private signalR: AranSignalRService) {
     this.signalR.connect();
     this.signalR.getCasualtyUpdates().subscribe(update => {
       this.updateCasualtyList(update.casualties);
     });
   }

   // ✅ NEW
   constructor(private polling: AranPollingService) {
     this.polling.startPolling();
   }

   // In template
   @for (casualty of polling.cases(); track casualty.caseNumber) { ... }
   ```

5. **Update environment configuration:**
   - Remove `signalRHub` from `environment.ts`
   - SignalR URL no longer needed (HTTP polling only)

---

## 10. Future Enhancements

### Possible Improvements
1. **Configurable Poll Interval:** Allow 30s / 60s / 120s intervals
2. **Offline Queue:** Queue updates when disconnected, sync on reconnect
3. **Progressive Polling:** Slower intervals during low activity, faster during incidents
4. **Data Compression:** Use differential updates (only send changed fields)
5. **Web Workers:** Run polling in background thread to avoid UI jank
6. **IndexedDB Cache:** Persist data locally for offline viewing
7. **Custom Alerts:** Configurable audio notifications by severity
8. **Analytics:** Track polling latency, error rates, alert frequency

### Monitoring & Observability
```typescript
// Add RxJS logging operator
.pipe(
  tap(data => console.log('Poll response:', data)),
  tap(_ => this.metricsService.recordPoll('success')),
  catchError(err => {
    this.metricsService.recordPoll('error', err.status);
    return of(null);
  })
)
```

---

## 11. API Reference (Quick)

### Service Creation
```typescript
constructor(private polling: AranPollingService) {}
```

### Start/Stop
```typescript
this.polling.startPolling();        // Start 60s polling
this.polling.stopPolling();         // Stop and cleanup
this.polling.refreshNow();          // Immediate poll
```

### Read Signals
```typescript
const cases = this.polling.cases();           // Casualty[]
const alert = this.polling.activeAlert();     // AranAlert | null
const syncing = this.polling.isSyncing();     // boolean
const lastTime = this.polling.lastSyncTime(); // string (HH:mm:ss)
const connected = this.polling.isConnected(); // boolean
const status = this.polling.connectionStatus(); // computed string
```

### In Template
```html
@for (casualty of polling.cases(); track casualty.caseNumber) { ... }
@if (polling.activeAlert(); as alert) { ... }
@if (polling.isSyncing()) { Syncing... }
{{ polling.connectionStatus() }}
[ngClass]="{'highlight': polling.updatedCaseIds().has(id)}"
```

---

## Conclusion

The **AranPollingService** provides a modern, maintainable replacement for WebSocket-based real-time updates. It's optimized for hospital networks, works on IIS sub-paths, and requires zero external assets (audio chimes generated programmatically). The Signal-based API ensures clean, reactive component code with automatic change detection.

**Key Advantages:**
✅ No external dependencies (Web Audio API built-in)
✅ Works on all networks (HTTP only, no WebSocket)
✅ Responsive to UI (no blocking operations)
✅ Hospital-grade alerts (audio + visual)
✅ Zero external audio files (offline compatible)
✅ Simple to test and debug
✅ IIS sub-path compatible out-of-the-box

For questions or issues, enable console logging and check the browser DevTools Network tab to verify API requests.
