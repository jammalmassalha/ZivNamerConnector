import { Injectable, OnDestroy } from '@angular/core';
import { BehaviorSubject, Observable, Subject, interval, Subscription } from 'rxjs';
import { switchMap, takeUntil, tap, catchError, startWith } from 'rxjs/operators';
import { AranHttpService } from './aran-http.service';
import { Casualty, KpiSummary } from '../models/aran.models';
import { of } from 'rxjs';

export interface DataChangeNotification {
  type: 'cases' | 'kpis';
  hasChanges: boolean;
  changeCount: number;
  message: string;
  timestamp: Date;
  affectedAccidentNo: string;
}

export interface PollingState {
  isPolling: boolean;
  lastPollTime: Date | null;
  pollCount: number;
  nextPollIn: number; // seconds
  error: string | null;
}

@Injectable({
  providedIn: 'root',
})
export class AranPollingService implements OnDestroy {
  private readonly POLLING_INTERVAL_MS = 60000; // 1 minute
  private readonly POLL_TIMEOUT_MS = 30000; // 30 seconds timeout

  // State management
  private readonly destroy$ = new Subject<void>();
  private readonly pollingActive$ = new BehaviorSubject<Map<string, boolean>>(new Map());
  private readonly pollingState$ = new BehaviorSubject<PollingState>({
    isPolling: false,
    lastPollTime: null,
    pollCount: 0,
    nextPollIn: this.POLLING_INTERVAL_MS / 1000,
    error: null,
  });

  // Data management - keep previous state to detect changes
  private currentCasualtyMap = new Map<string, Casualty[]>();
  private currentKpiMap = new Map<string, KpiSummary>();

  // Notifications
  private readonly dataChanged$ = new BehaviorSubject<DataChangeNotification | null>(null);
  private readonly notifications$ = new Subject<DataChangeNotification>();

  constructor(private aranHttp: AranHttpService) {
    this.initializePollingTimer();
  }

  /**
   * Start polling for a specific incident
   */
  startPolling(accidentNo: string): Subscription {
    if (!accidentNo) {
      console.error('AccidentNo is required to start polling');
      return new Subscription();
    }

    const activePolls = this.pollingActive$.value;
    if (activePolls.has(accidentNo) && activePolls.get(accidentNo)) {
      console.warn(`Polling already active for ${accidentNo}`);
      return new Subscription();
    }

    activePolls.set(accidentNo, true);
    this.pollingActive$.next(activePolls);

    // Set up interval-based polling
    const subscription = interval(this.POLLING_INTERVAL_MS)
      .pipe(
        startWith(0), // Poll immediately on start
        switchMap(() => this.pollData(accidentNo)),
        tap(() => this.updatePollingState()),
        catchError((error) => {
          console.error(`Polling error for ${accidentNo}:`, error);
          this.updatePollingState(error.message);
          return of(null);
        }),
        takeUntil(this.destroy$)
      )
      .subscribe();

    return subscription;
  }

  /**
   * Stop polling for a specific incident
   */
  stopPolling(accidentNo: string): void {
    const activePolls = this.pollingActive$.value;
    activePolls.set(accidentNo, false);
    this.pollingActive$.next(activePolls);
  }

  /**
   * Poll data for an incident (both cases and KPIs)
   */
  private pollData(accidentNo: string): Observable<any> {
    return new Observable((observer) => {
      const startTime = Date.now();
      const pollTimeout = setTimeout(() => {
        observer.error(new Error('Polling timeout'));
      }, this.POLL_TIMEOUT_MS);

      Promise.all([
        this.aranHttp.getCases(accidentNo).toPromise(),
        this.aranHttp.getKpis(accidentNo).toPromise(),
      ])
        .then(([cases, kpis]) => {
          clearTimeout(pollTimeout);
          const pollDuration = Date.now() - startTime;

          // Check for changes in cases
          this.checkCasesChanged(accidentNo, cases || []);

          // Check for changes in KPIs
          this.checkKpisChanged(accidentNo, kpis || null);

          observer.next({
            accidentNo,
            cases,
            kpis,
            pollDuration,
            timestamp: new Date(),
          });
          observer.complete();
        })
        .catch((error) => {
          clearTimeout(pollTimeout);
          observer.error(error);
        });
    });
  }

  /**
   * Detect changes in casualty data
   */
  private checkCasesChanged(accidentNo: string, newCases: Casualty[]): void {
    const previousCases = this.currentCasualtyMap.get(accidentNo) || [];
    this.currentCasualtyMap.set(accidentNo, newCases);

    if (previousCases.length === 0) {
      // First load, don't notify
      return;
    }

    // Build maps for comparison
    const previousMap = new Map(previousCases.map((c) => [c.caseNumber, c]));
    const newMap = new Map(newCases.map((c) => [c.caseNumber, c]));

    let changeCount = 0;
    const changes: string[] = [];

    // Check for new or modified cases
    newMap.forEach((newCase, caseNumber) => {
      const previousCase = previousMap.get(caseNumber);
      if (!previousCase) {
        changeCount++;
        changes.push(`🆕 New case: ${caseNumber}`);
      } else if (previousCase.triageStatus !== newCase.triageStatus) {
        changeCount++;
        changes.push(`🔄 Triage changed: ${caseNumber} (${previousCase.triageStatus} → ${newCase.triageStatus})`);
      } else if (previousCase.currentLocation !== newCase.currentLocation) {
        changeCount++;
        changes.push(`📍 Location changed: ${caseNumber}`);
      }
    });

    // Check for removed cases
    previousMap.forEach((_, caseNumber) => {
      if (!newMap.has(caseNumber)) {
        changeCount++;
        changes.push(`❌ Case removed: ${caseNumber}`);
      }
    });

    if (changeCount > 0) {
      const notification: DataChangeNotification = {
        type: 'cases',
        hasChanges: true,
        changeCount,
        message: `${changeCount} change${changeCount > 1 ? 's' : ''} detected in casualty cases`,
        timestamp: new Date(),
        affectedAccidentNo: accidentNo,
      };

      this.dataChanged$.next(notification);
      this.notifications$.next(notification);

      console.log(`Cases changed for ${accidentNo}:`, changes);
    }
  }

  /**
   * Detect changes in KPI data
   */
  private checkKpisChanged(accidentNo: string, newKpi: KpiSummary | null): void {
    if (!newKpi) return;

    const previousKpi = this.currentKpiMap.get(accidentNo);
    this.currentKpiMap.set(accidentNo, newKpi);

    if (!previousKpi) {
      // First load, don't notify
      return;
    }

    const changes: string[] = [];
    let changeCount = 0;

    // Compare KPI values
    if (previousKpi.red !== newKpi.red) {
      changeCount++;
      changes.push(`🔴 Red: ${previousKpi.red} → ${newKpi.red}`);
    }
    if (previousKpi.yellow !== newKpi.yellow) {
      changeCount++;
      changes.push(`🟡 Yellow: ${previousKpi.yellow} → ${newKpi.yellow}`);
    }
    if (previousKpi.green !== newKpi.green) {
      changeCount++;
      changes.push(`🟢 Green: ${previousKpi.green} → ${newKpi.green}`);
    }
    if (previousKpi.black !== newKpi.black) {
      changeCount++;
      changes.push(`⚫ Black: ${previousKpi.black} → ${newKpi.black}`);
    }
    if (previousKpi.inTreatment !== newKpi.inTreatment) {
      changeCount++;
      changes.push(`🏥 In Treatment: ${previousKpi.inTreatment} → ${newKpi.inTreatment}`);
    }

    if (changeCount > 0) {
      const notification: DataChangeNotification = {
        type: 'kpis',
        hasChanges: true,
        changeCount,
        message: `KPI updated: ${changeCount} metric${changeCount > 1 ? 's' : ''} changed`,
        timestamp: new Date(),
        affectedAccidentNo: accidentNo,
      };

      this.dataChanged$.next(notification);
      this.notifications$.next(notification);

      console.log(`KPIs changed for ${accidentNo}:`, changes);
    }
  }

  /**
   * Get data change notifications stream
   */
  getNotifications(): Observable<DataChangeNotification> {
    return this.notifications$.asObservable();
  }

  /**
   * Get latest data change notification
   */
  getLatestNotification(): Observable<DataChangeNotification | null> {
    return this.dataChanged$.asObservable();
  }

  /**
   * Get current polling state
   */
  getPollingState(): Observable<PollingState> {
    return this.pollingState$.asObservable();
  }

  /**
   * Check if polling is active for an incident
   */
  isPollingActive(accidentNo: string): boolean {
    return this.pollingActive$.value.get(accidentNo) || false;
  }

  /**
   * Get all active polling incidents
   */
  getActivePollingIncidents(): string[] {
    return Array.from(this.pollingActive$.value.entries())
      .filter(([_, isActive]) => isActive)
      .map(([accidentNo]) => accidentNo);
  }

  /**
   * Update polling state display
   */
  private updatePollingState(error: string | null = null): void {
    const currentState = this.pollingState$.value;
    const newState: PollingState = {
      isPolling: this.getActivePollingIncidents().length > 0,
      lastPollTime: new Date(),
      pollCount: currentState.pollCount + 1,
      nextPollIn: this.POLLING_INTERVAL_MS / 1000,
      error,
    };
    this.pollingState$.next(newState);
  }

  /**
   * Initialize polling state timer for displaying countdown
   */
  private initializePollingTimer(): void {
    interval(1000)
      .pipe(takeUntil(this.destroy$))
      .subscribe(() => {
        const currentState = this.pollingState$.value;
        if (currentState.isPolling && currentState.nextPollIn > 0) {
          this.pollingState$.next({
            ...currentState,
            nextPollIn: Math.max(0, currentState.nextPollIn - 1),
          });
        }
      });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }
}
