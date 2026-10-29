# Angular Polling Service & Notification System

## Overview

Replaced WebSocket/SignalR with an efficient **polling-based system** that fetches data via POST requests every 60 seconds and intelligently detects changes with beautiful, non-intrusive notifications.

### Why Polling Over WebSocket?

✅ **Advantages:**
- Simpler infrastructure (no WebSocket/SignalR server required)
- Works behind corporate firewalls/proxies
- Easier to debug and test
- Lower server resource overhead for large numbers of clients
- Graceful degradation (fails safely if API is down)
- Works on all browsers (WebSocket has compatibility issues on some)

⚠️ **Trade-offs:**
- Slightly higher latency (up to 1 minute delay)
- More HTTP requests
- Fixed polling interval (configurable)

---

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                       Angular App                            │
├─────────────────────────────────────────────────────────────┤
│                                                               │
│  Component using data:                                       │
│  ├─ Subscribes to polling service                           │
│  └─ Receives notifications when data changes                │
│                                                               │
│  AranPollingService (core):                                  │
│  ├─ Manages polling intervals (every 60s)                    │
│  ├─ Calls API via HTTP POST requests                        │
│  ├─ Detects data changes by comparing states                │
│  └─ Emits notifications when changes detected               │
│                                                               │
│  DataNotificationPanelComponent (UI):                        │
│  ├─ Displays alert cards (top-right)                        │
│  ├─ Shows polling status (live/inactive)                    │
│  ├─ Auto-dismisses after 5 seconds                          │
│  └─ Plays notification sound                                 │
│                                                               │
└─────────────────────────────────────────────────────────────┘
                            ↓
            ┌───────────────────────────────┐
            │   Backend API (SapSync.Api)   │
            ├───────────────────────────────┤
            │ POST /api/aran/cases          │
            │ POST /api/aran/kpis           │
            │ Returns current data          │
            └───────────────────────────────┘
```

---

## Installation & Setup

### 1. Add Components to Your App

**In `app.component.ts`:**

```typescript
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { DataNotificationPanelComponent } from './components/data-notification-panel/data-notification-panel.component';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, DataNotificationPanelComponent],
  template: `
    <div class="app">
      <!-- Your existing app content -->
      
      <!-- Add this line to show notifications -->
      <app-data-notification-panel></app-data-notification-panel>
    </div>
  `,
})
export class AppComponent {}
```

### 2. Enable Polling in a Component

**Example: In your incident dashboard component:**

```typescript
import { Component, OnInit, OnDestroy } from '@angular/core';
import { AranPollingService } from './services/aran-polling.service';
import { Subscription } from 'rxjs';

@Component({
  selector: 'app-incident-dashboard',
  template: `
    <div class="dashboard">
      <h1>Incident {{ accidentNo }}</h1>
      
      <!-- Your UI here -->
      
      <!-- Casualties list -->
      <div *ngFor="let casualty of casualties$ | async">
        {{ casualty.caseNumber }}
      </div>
    </div>
  `,
})
export class IncidentDashboardComponent implements OnInit, OnDestroy {
  accidentNo = '123456'; // Your incident number
  casualties$ = this.aran.getCases(this.accidentNo);
  
  private pollingSubscription: Subscription | null = null;

  constructor(private aran: AranPollingService) {}

  ngOnInit(): void {
    // Start polling for this incident
    this.pollingSubscription = this.aran.startPolling(this.accidentNo);
  }

  ngOnDestroy(): void {
    // Stop polling when component is destroyed
    this.aran.stopPolling(this.accidentNo);
    this.pollingSubscription?.unsubscribe();
  }
}
```

---

## API Reference

### `AranPollingService`

#### Methods

##### `startPolling(accidentNo: string): Subscription`
Start polling for an incident.

```typescript
// Start polling
const subscription = this.pollingService.startPolling('INC001');

// Stop polling when done
subscription.unsubscribe();
// OR
this.pollingService.stopPolling('INC001');
```

##### `stopPolling(accidentNo: string): void`
Stop polling for a specific incident.

```typescript
this.pollingService.stopPolling('INC001');
```

##### `getNotifications(): Observable<DataChangeNotification>`
Subscribe to all notifications (with debounce).

```typescript
this.pollingService.getNotifications().subscribe((notification) => {
  console.log('Data changed:', notification);
});
```

##### `getLatestNotification(): Observable<DataChangeNotification | null>`
Subscribe to the latest notification only.

```typescript
this.pollingService.getLatestNotification().subscribe((notification) => {
  if (notification) {
    console.log('Latest change:', notification.message);
  }
});
```

##### `getPollingState(): Observable<PollingState>`
Subscribe to polling status and metrics.

```typescript
this.pollingService.getPollingState().subscribe((state) => {
  console.log('Polling active:', state.isPolling);
  console.log('Total polls:', state.pollCount);
  console.log('Last poll:', state.lastPollTime);
  console.log('Next poll in:', state.nextPollIn, 'seconds');
  console.log('Error:', state.error);
});
```

##### `isPollingActive(accidentNo: string): boolean`
Check if polling is running for a specific incident.

```typescript
const isActive = this.pollingService.isPollingActive('INC001');
```

##### `getActivePollingIncidents(): string[]`
Get list of all incidents currently being polled.

```typescript
const incidents = this.pollingService.getActivePollingIncidents();
console.log('Polling for:', incidents); // ['INC001', 'INC002']
```

---

## Data Change Notifications

### DataChangeNotification Interface

```typescript
interface DataChangeNotification {
  type: 'cases' | 'kpis';           // Type of data changed
  hasChanges: boolean;                // Always true when emitted
  changeCount: number;                // Number of changes detected
  message: string;                    // Human-readable message
  timestamp: Date;                    // When change was detected
  affectedAccidentNo: string;         // Which incident changed
}
```

### Example Notifications

**Case Changed:**
```json
{
  "type": "cases",
  "hasChanges": true,
  "changeCount": 2,
  "message": "2 changes detected in casualty cases",
  "timestamp": "2026-10-29T12:00:00Z",
  "affectedAccidentNo": "INC001"
}
```

**KPI Changed:**
```json
{
  "type": "kpis",
  "hasChanges": true,
  "changeCount": 3,
  "message": "KPI updated: 3 metrics changed",
  "timestamp": "2026-10-29T12:01:00Z",
  "affectedAccidentNo": "INC001"
}
```

---

## UI Components

### DataNotificationPanelComponent

Displays notifications with three layers:

#### 1. **Alert Cards** (Main)
- Show on the right side
- 4-5 second auto-dismiss
- Smooth slide-in animation
- Progress bar showing dismiss countdown
- Click X to dismiss manually

#### 2. **Status Bar** (Live Metrics)
- Shows polling status (active/inactive)
- Poll counter
- Time since last poll
- Countdown to next poll
- Error display if any

#### 3. **Toast Notifications** (Quick Alert)
- Quick flash notification
- 3-second display
- Appears above alert cards
- Less intrusive

### Usage in Template

```typescript
// Automatically displays when you add this component
<app-data-notification-panel></app-data-notification-panel>

// In parent component, no additional config needed!
```

---

## Configuration

### Polling Interval

**Default:** 60 seconds (1 minute)

**To change polling interval:**
Edit `AranPollingService.POLLING_INTERVAL_MS`:

```typescript
private readonly POLLING_INTERVAL_MS = 60000; // Change to your interval (milliseconds)
```

Example intervals:
- 30 seconds: `30000`
- 2 minutes: `120000`
- 5 minutes: `300000`

### Alert Auto-Dismiss Time

**Default:** 5 seconds

**To change:** In `DataNotificationPanelComponent`, update:

```typescript
// Line ~100 in addNotification()
const timeout = setTimeout(() => {
  this.dismissAlert(alertId);
}, 5000); // Change to your value (milliseconds)
```

### Notification Sound

**Enabled by default**

To disable, comment out this line in `DataNotificationPanelComponent`:
```typescript
// this.playNotificationSound();
```

---

## Change Detection Logic

### Cases Change Detection

Detects when:
- ✅ **New case** appears
- ✅ **Case removed** from list
- ✅ **Triage status changed** (RED → YELLOW, etc.)
- ✅ **Location changed**
- ❌ Minor updates (timestamps, non-critical fields)

### KPI Change Detection

Detects changes in:
- ✅ Red count
- ✅ Yellow count
- ✅ Green count
- ✅ Black count
- ✅ In treatment count

---

## Best Practices

### 1. Start Polling in `ngOnInit`, Stop in `ngOnDestroy`

```typescript
ngOnInit() {
  this.pollingSubscription = this.pollingService.startPolling(this.accidentNo);
}

ngOnDestroy() {
  this.pollingService.stopPolling(this.accidentNo);
  this.pollingSubscription?.unsubscribe();
}
```

### 2. Use OnDestroy Interface

Always implement `OnDestroy` to clean up polling when component is destroyed.

### 3. Handle Multiple Incidents

If tracking multiple incidents, start polling for each:

```typescript
const incidents = ['INC001', 'INC002', 'INC003'];

incidents.forEach(incident => {
  this.pollingService.startPolling(incident);
});

// Later, stop all
incidents.forEach(incident => {
  this.pollingService.stopPolling(incident);
});
```

### 4. Filter Notifications by Type

```typescript
this.pollingService.getNotifications()
  .pipe(
    filter(n => n.type === 'cases'), // Only case changes
    // or: filter(n => n.type === 'kpis'), // Only KPI changes
  )
  .subscribe(notification => {
    console.log('Case updated:', notification);
  });
```

### 5. Monitor Polling Health

```typescript
this.pollingService.getPollingState().subscribe(state => {
  if (state.error) {
    console.error('Polling error:', state.error);
    // Could re-enable polling, show user warning, etc.
  }
  
  if (!state.isPolling) {
    console.warn('Polling stopped unexpectedly');
  }
});
```

---

## Troubleshooting

### Issue: Notifications not appearing

**Check:**
1. Is `DataNotificationPanelComponent` added to your app root?
2. Is polling started with `startPolling(accidentNo)`?
3. Check browser console for errors
4. Verify API endpoint is working: `curl http://pbi_srv:546/api/aran/cases?accidentNo=INC001`

### Issue: Polling stops without error

**Check:**
1. Component is still mounted (not destroyed)
2. Check `ngOnDestroy` isn't stopping it prematurely
3. Check for browser tab becoming inactive (some browsers throttle async tasks)

### Issue: Too many notifications

**Solution:**
- Increase polling interval (default 60s is usually good)
- Add debounce to reduce spam (already included)
- Adjust alert auto-dismiss time

### Issue: API calls failing

**Check:**
1. Environment API URL is correct: `environment.apiUrl`
2. API is accessible: `curl http://pbi_srv:546/api/aran/kpis?accidentNo=INC001`
3. CORS is enabled on backend (if cross-origin)
4. Network tab in DevTools shows error details

---

## Performance

### Memory Usage
- Keeps one copy of data per incident in memory
- Maps are cleaned up on component destroy
- Alert timeouts cleared automatically

### Network Usage
- 1 HTTP POST request every 60 seconds per incident
- Small payload (~1-2 KB per incident)
- Example: 10 incidents = 10 requests/minute = ~20 KB/min

### CPU Usage
- Minimal: only runs when polling interval triggers
- Change detection only compares fields that matter
- Debounced to prevent spam processing

---

## Migration from SignalR

### Old Way (WebSocket):
```typescript
// Old
this.signalR.getCasualtyUpdates().subscribe(update => {
  // Update received in real-time (instant)
});
```

### New Way (Polling):
```typescript
// New
this.pollingService.startPolling(accidentNo);
this.pollingService.getNotifications().subscribe(notification => {
  // Change detected (within 60 seconds)
});
```

**Key differences:**
- Notifications fire on poll intervals, not instantly
- Less network overhead overall
- Simpler infrastructure
- Better reliability

---

## Examples

### Example 1: Simple Incident Monitor

```typescript
@Component({
  selector: 'app-incident-monitor',
  template: `
    <div *ngIf="pollingState$ | async as state">
      <span *ngIf="state.isPolling">🟢 Live</span>
      <span *ngIf="!state.isPolling">⚫ Offline</span>
      Polls: {{ state.pollCount }} | Last: {{ state.lastPollTime | date:'HH:mm:ss' }}
    </div>
  `,
})
export class IncidentMonitorComponent {
  pollingState$ = this.pollingService.getPollingState();

  constructor(private pollingService: AranPollingService, private route: ActivatedRoute) {
    this.route.params.subscribe(params => {
      this.pollingService.startPolling(params['accidentNo']);
    });
  }
}
```

### Example 2: Alert Badge

```typescript
@Component({
  selector: 'app-alert-badge',
  template: `
    <div class="badge" *ngIf="unreadNotifications > 0">
      {{ unreadNotifications }}
      <div class="latest-message">{{ latestMessage }}</div>
    </div>
  `,
})
export class AlertBadgeComponent {
  unreadNotifications = 0;
  latestMessage = '';

  constructor(private pollingService: AranPollingService) {
    this.pollingService.getNotifications().subscribe(notification => {
      this.unreadNotifications++;
      this.latestMessage = notification.message;
      
      // Auto-clear after 10 seconds
      setTimeout(() => {
        this.unreadNotifications = 0;
      }, 10000);
    });
  }
}
```

### Example 3: Data Refresh on Notification

```typescript
export class CasualtyListComponent implements OnInit {
  casualties$ = this.aran.getCases(this.accidentNo);

  constructor(
    private pollingService: AranPollingService,
    private aran: AranHttpService
  ) {}

  ngOnInit(): void {
    this.pollingService.startPolling(this.accidentNo);

    // Refresh data whenever notification fires
    this.pollingService.getNotifications().subscribe(() => {
      this.casualties$ = this.aran.getCases(this.accidentNo);
    });
  }
}
```

---

## Files

- **Service:** `src/app/core/services/aran-polling.service.ts`
- **Component:** `src/app/components/data-notification-panel/data-notification-panel.component.ts`
- **HTTP Service:** `src/app/core/services/aran-http.service.ts` (uses POST requests)

---

## Related Documentation

- [Angular API Configuration](./ANGULAR_API_CONFIG_GUIDE.md)
- [Backend API](../SapSyncSolution/DELTA_SYNC_IMPLEMENTATION.md)
- [IIS Deployment](../SapSyncSolution/IIS_DEPLOYMENT_GUIDE.md)

---

**Last Updated:** 2026-10-29
**Status:** Ready for production
**Tested with:** Angular 18+, TypeScript 5.2+
