import { Injectable, signal, computed, effect, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { timer, Subscription, switchMap, catchError, of, tap, Subject } from 'rxjs';
import { Casualty, KpiSummary } from '../models/aran.models';

export interface AranAlert {
  id: string;
  title: string;
  message: string;
  type: 'critical' | 'new' | 'update';
  timestamp: string;
}

@Injectable({ providedIn: 'root' })
export class AranPollingService {
  private readonly http = inject(HttpClient);

  // Signal-based state management
  readonly cases = signal<Casualty[]>([]);
  readonly activeAlert = signal<AranAlert | null>(null);
  readonly updatedCaseIds = signal<Set<string>>(new Set());
  readonly isSyncing = signal<boolean>(false);
  readonly lastSyncTime = signal<string>('--:--:--');
  readonly isConnected = signal<boolean>(true);

  // Computed signals
  readonly hasActiveAlert = computed(() => this.activeAlert() !== null);
  readonly connectionStatus = computed(() => {
    if (this.isSyncing()) return 'סנכרון פעיל (כל 60 שנ׳)';
    if (this.isConnected()) return 'מחובר בזמן אמת';
    return 'מנותק - לחץ לרענון';
  });

  private pollingSubscription?: Subscription;
  private destroy$ = new Subject<void>();

  constructor() {
    // Log initialization
    const apiUrl = this.getApiUrl();
    console.log('[AranPollingService] Initialized with API URL:', apiUrl);
  }

  /**
   * Resolves API URL respecting IIS sub-directory (/pbi_srv/zivarantest/)
   */
  private getApiUrl(): string {
    const base = document.querySelector('base')?.getAttribute('href')?.replace(/\/$/, '') || '';
    return `${base}/api/aran/cases`;
  }

  /**
   * Start polling for cases immediately and every 60 seconds
   */
  startPolling(): void {
    if (this.pollingSubscription) return;

    console.log('[AranPollingService] Starting polling...');

    // Trigger immediately (0ms) and repeat every 60,000ms (1 min)
    this.pollingSubscription = timer(0, 60000)
      .pipe(
        tap(() => this.isSyncing.set(true)),
        switchMap(() =>
          this.http.get<Casualty[]>(this.getApiUrl()).pipe(
            catchError((err) => {
              console.error('[AranPollingService] Failed to fetch ARAN cases:', err);
              this.isConnected.set(false);
              this.isSyncing.set(false);
              return of(null);
            })
          )
        )
      )
      .subscribe((incomingCases) => {
        this.isSyncing.set(false);
        if (!incomingCases) return;

        this.isConnected.set(true);
        this.lastSyncTime.set(new Date().toLocaleTimeString('he-IL'));
        this.processDelta(incomingCases);
      });
  }

  /**
   * Stop polling and clean up subscriptions
   */
  stopPolling(): void {
    this.pollingSubscription?.unsubscribe();
    this.pollingSubscription = undefined;
    console.log('[AranPollingService] Polling stopped');
  }

  /**
   * Trigger an immediate refresh cycle
   */
  refreshNow(): void {
    console.log('[AranPollingService] Manual refresh requested');
    this.isSyncing.set(true);
    this.http
      .get<Casualty[]>(this.getApiUrl())
      .pipe(
        catchError((err) => {
          console.error('[AranPollingService] Refresh failed:', err);
          this.isConnected.set(false);
          return of(null);
        })
      )
      .subscribe((incomingCases) => {
        this.isSyncing.set(false);
        if (!incomingCases) return;

        this.isConnected.set(true);
        this.lastSyncTime.set(new Date().toLocaleTimeString('he-IL'));
        this.processDelta(incomingCases);
      });
  }

  /**
   * Detects changes between previous cases and new API payload
   * Triggers audio/visual alerts on new casualties or triage escalations
   */
  private processDelta(newCases: Casualty[]): void {
    const currentMap = new Map(this.cases().map((c) => [c.caseNumber, c]));
    const changedIds = new Set<string>();
    let newVictimsCount = 0;
    let criticalEscalations = 0;

    for (const item of newCases) {
      const existing = currentMap.get(item.caseNumber);
      if (!existing) {
        // Brand new casualty arrived
        newVictimsCount++;
        changedIds.add(item.caseNumber);
        console.log(`[AranPollingService] New casualty: ${item.caseNumber}`);
      } else if (
        existing.triageStatus !== item.triageStatus ||
        existing.currentMovement !== item.currentMovement ||
        existing.currentLocation !== item.currentLocation
      ) {
        // Casualty updated (severity or room transfer)
        if (item.triageStatus === 'RED' && existing.triageStatus !== 'RED') {
          criticalEscalations++;
          console.log(
            `[AranPollingService] Critical escalation: ${item.caseNumber} → RED`
          );
        }
        changedIds.add(item.caseNumber);
      }
    }

    // Trigger visual/audio alerts if changes detected (skips on initial load)
    if (currentMap.size > 0 && (newVictimsCount > 0 || changedIds.size > 0)) {
      this.triggerAlert(newVictimsCount, criticalEscalations, changedIds);
    }

    // Update state
    this.cases.set(newCases);
  }

  /**
   * Trigger audio chime + visual notifications + row highlight
   */
  private triggerAlert(
    newCount: number,
    criticalCount: number,
    changedIds: Set<string>
  ): void {
    // 1. Play Sound via Web Audio API (hospital offline compatible)
    this.playHospitalChime(criticalCount > 0);

    // 2. Set Row Flash Highlight
    this.updatedCaseIds.set(changedIds);
    setTimeout(() => this.updatedCaseIds.set(new Set()), 4000); // clear after 4s

    // 3. Set Floating Toast Alert
    const title =
      criticalCount > 0 ? 'התראת טריאז׳ קריטי!' : 'עדכון נפגעים בזמן אמת';
    const message =
      newCount > 0
        ? `נקלטו ${newCount} נפגעים חדשים באירוע`
        : `עודכנו נתוני שהייה ומיקום עבור ${changedIds.size} מטופלים`;

    this.activeAlert.set({
      id: Date.now().toString(),
      title,
      message,
      type: criticalCount > 0 ? 'critical' : 'new',
      timestamp: new Date().toLocaleTimeString('he-IL'),
    });

    // Auto-dismiss alert after 6 seconds
    setTimeout(() => this.activeAlert.set(null), 6000);
  }

  /**
   * Play hospital chime sound using HTML5 Web Audio API
   * No external MP3 files required - works completely offline
   */
  private playHospitalChime(isCritical: boolean): void {
    try {
      const ctx = new (window.AudioContext || (window as any).webkitAudioContext)();
      const osc = ctx.createOscillator();
      const gain = ctx.createGain();

      // Critical = triangle wave at higher frequency (A5 -> A4 sweep)
      // Standard = sine wave at lower frequency (D5 -> A5 sweep)
      osc.type = isCritical ? 'triangle' : 'sine';
      osc.frequency.setValueAtTime(
        isCritical ? 880 : 587.33,
        ctx.currentTime
      ); // A5 or D5
      osc.frequency.exponentialRampToValueAtTime(
        isCritical ? 440 : 880,
        ctx.currentTime + 0.3
      );

      // Fade in/out with exponential ramp
      gain.gain.setValueAtTime(0.2, ctx.currentTime);
      gain.gain.exponentialRampToValueAtTime(0.01, ctx.currentTime + 0.4);

      osc.connect(gain);
      gain.connect(ctx.destination);

      osc.start();
      osc.stop(ctx.currentTime + 0.4);

      console.log(
        `[AranPollingService] Played ${isCritical ? 'critical' : 'standard'} chime`
      );
    } catch (e) {
      // AudioContext blocked by browser autoplay policy until first click
      console.log('[AranPollingService] AudioContext unavailable:', e);
    }
  }

  /**
   * Clean up on service destroy
   */
  ngOnDestroy(): void {
    this.stopPolling();
    this.destroy$.next();
    this.destroy$.complete();
  }
}
