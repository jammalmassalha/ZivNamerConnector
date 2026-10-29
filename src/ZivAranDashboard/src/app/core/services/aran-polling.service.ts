import { Injectable, OnDestroy, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Subscription, catchError, exhaustMap, filter, of, tap, timer } from 'rxjs';
import { Casualty, KpiSummary } from '../models/aran.models';
import { environment } from '../../../environments/environment';

export interface AranAlert {
  id: string;
  title: string;
  message: string;
  type: 'critical' | 'new' | 'update';
  timestamp: Date;
}

interface PollResult {
  casualties: Casualty[];
  kpi: KpiSummary | null;
  serverTime: string;
}

export const POLL_INTERVAL_MS = 60_000;
const ALERT_TTL_MS = 8_000;
const HIGHLIGHT_TTL_MS = 5_000;
const MAX_ALERTS = 3;

/** Polls POST /api/aran/poll every minute and diffs results to raise alerts. */
@Injectable({ providedIn: 'root' })
export class AranPollingService implements OnDestroy {
  private readonly http = inject(HttpClient);

  readonly casualties = signal<Casualty[]>([]);
  readonly kpi = signal<KpiSummary | null>(null);
  readonly alerts = signal<AranAlert[]>([]);
  readonly updatedCaseIds = signal<Set<string>>(new Set());
  readonly isSyncing = signal(false);
  readonly isConnected = signal(true);
  readonly lastSync = signal<Date | null>(null);
  readonly nextSync = signal<Date | null>(null);

  private sub?: Subscription;
  private accidentNo = '';
  private search = '';
  private initialLoadDone = false;
  private audioCtx?: AudioContext;

  start(accidentNo: string, search = ''): void {
    this.stop();
    this.accidentNo = accidentNo;
    this.search = search;
    this.initialLoadDone = false;
    this.casualties.set([]);
    this.kpi.set(null);
    this.lastSync.set(null);
    this.isSyncing.set(!!accidentNo);
    if (!accidentNo) return;

    // exhaustMap prevents overlapping requests if the server is slow
    this.sub = timer(0, POLL_INTERVAL_MS)
      .pipe(exhaustMap(() => this.fetch()))
      .subscribe((result) => this.handle(result));
  }

  stop(): void {
    this.sub?.unsubscribe();
    this.sub = undefined;
    this.nextSync.set(null);
  }

  setSearch(search: string): void {
    this.search = search;
    this.refreshNow();
  }

  refreshNow(): void {
    if (!this.accidentNo) return;
    this.fetch().subscribe((r) => this.handle(r));
  }

  dismiss(id: string): void {
    this.alerts.update((list) => list.filter((a) => a.id !== id));
  }

  ngOnDestroy(): void {
    this.stop();
  }

  private fetch() {
    this.isSyncing.set(true);
    const requested = this.accidentNo;
    const url = `${environment.apiUrl}/aran/poll`;
    console.log('[AranPolling] POST', url, { accidentNo: requested, search: this.search });
    return this.http
      .post<PollResult>(url, {
        accidentNo: requested,
        search: this.search || null,
      })
      .pipe(
        tap((res) => console.log('[AranPolling] response for', requested, res)),
        catchError((err) => {
          console.error('[AranPolling] request failed', err);
          return of(null);
        }),
        // Drop stale responses from a previously selected incident
        filter(() => requested === this.accidentNo)
      );
  }

  private handle(result: PollResult | null): void {
    this.isSyncing.set(false);
    this.nextSync.set(new Date(Date.now() + POLL_INTERVAL_MS));
    if (!result) {
      this.isConnected.set(false);
      return;
    }
    this.isConnected.set(true);
    this.lastSync.set(new Date());
    this.kpi.set(result.kpi);
    this.diff(result.casualties ?? []);
    this.casualties.set(result.casualties ?? []);
  }

  private diff(incoming: Casualty[]): void {
    if (!this.initialLoadDone) {
      this.initialLoadDone = true;
      return;
    }
    const prev = new Map(this.casualties().map((c) => [c.caseNumber, c]));
    const changed = new Set<string>();
    let added = 0;
    let escalations = 0;

    for (const c of incoming) {
      const old = prev.get(c.caseNumber);
      if (!old) {
        added++;
        changed.add(c.caseNumber);
        if (c.triageStatus === 'RED') escalations++;
      } else if (
        old.triageStatus !== c.triageStatus ||
        old.currentMovement !== c.currentMovement ||
        old.currentLocation !== c.currentLocation
      ) {
        changed.add(c.caseNumber);
        if (c.triageStatus === 'RED' && old.triageStatus !== 'RED') escalations++;
      }
    }
    if (changed.size === 0) return;

    const updated = changed.size - added;
    const parts: string[] = [];
    if (added) parts.push(`${added} נפגעים חדשים`);
    if (updated) parts.push(`${updated} עדכוני סטטוס/מיקום`);

    this.pushAlert({
      id: `${Date.now()}`,
      type: escalations ? 'critical' : added ? 'new' : 'update',
      title: escalations ? `התראה: ${escalations} נפגעים במצב אדום` : 'עדכון נתונים',
      message: parts.join(' • '),
      timestamp: new Date(),
    });

    this.updatedCaseIds.set(changed);
    setTimeout(() => this.updatedCaseIds.set(new Set()), HIGHLIGHT_TTL_MS);
    this.chime(escalations > 0);
  }

  private pushAlert(alert: AranAlert): void {
    this.alerts.update((list) => [alert, ...list].slice(0, MAX_ALERTS));
    // Critical alerts stay until dismissed
    if (alert.type !== 'critical') {
      setTimeout(() => this.dismiss(alert.id), ALERT_TTL_MS);
    }
  }

  private chime(critical: boolean): void {
    try {
      this.audioCtx ??= new (window.AudioContext || (window as any).webkitAudioContext)();
      const ctx = this.audioCtx;
      const osc = ctx.createOscillator();
      const gain = ctx.createGain();
      osc.type = critical ? 'triangle' : 'sine';
      osc.frequency.setValueAtTime(critical ? 880 : 587.33, ctx.currentTime);
      osc.frequency.exponentialRampToValueAtTime(critical ? 440 : 880, ctx.currentTime + 0.3);
      gain.gain.setValueAtTime(0.2, ctx.currentTime);
      gain.gain.exponentialRampToValueAtTime(0.01, ctx.currentTime + 0.4);
      osc.connect(gain).connect(ctx.destination);
      osc.start();
      osc.stop(ctx.currentTime + 0.4);
    } catch {
      // Autoplay policy may block audio until first user interaction
    }
  }
}
