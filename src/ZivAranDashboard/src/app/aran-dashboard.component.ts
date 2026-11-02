import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Subscription, forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { AranService } from './core/services/aran.service';
import {
  AranDashboardPayload,
  AranPatientCase,
  AranPatientUpdate,
  IncidentOption,
} from './core/models/aran.models';
import { ZivHeaderComponent } from './shared/components/ziv-header/ziv-header.component';

type TriageFilter = 'ALL' | 'RED' | 'YELLOW' | 'GREEN' | 'ADMITTED';
type ToastKind = 'new' | 'update' | 'incident' | 'error';

export interface DashboardToast {
  id: number;
  kind: ToastKind;
  title: string;
  message: string;
  accidentNo?: string;
  actionLabel?: string;
}

const BACKGROUND_REFRESH_INTERVAL_MS = 15_000;
const ROW_HIGHLIGHT_MS = 12_000;
const TOAST_LIFETIME_MS = 10_000;
const MAX_TOASTS = 4;

@Component({
  selector: 'app-aran-dashboard',
  standalone: true,
  imports: [CommonModule, FormsModule, ZivHeaderComponent],
  templateUrl: './aran-dashboard.component.html',
})
export class AranDashboardComponent implements OnInit, OnDestroy {
  readonly incidents = signal<IncidentOption[]>([]);
  readonly selectedIncident = signal<IncidentOption | null>(null);
  readonly cases = signal<AranPatientCase[]>([]);
  readonly searchText = signal('');
  readonly triageFilter = signal<TriageFilter>('ALL');
  readonly activeCase = signal<AranPatientCase | null>(null);
  readonly systemTime = signal(new Date());
  readonly isLoading = signal(false);
  readonly isSyncing = signal(false);
  readonly lastSyncedAt = signal<Date | null>(null);
  readonly syncFailed = signal(false);
  readonly isConnected = signal(false);
  readonly connectionPulse = signal(false);
  readonly errorMessage = signal('');
  readonly newCaseIds = signal<Set<string>>(new Set());
  readonly highlightedCaseIds = signal<Set<string>>(new Set());
  readonly toasts = signal<DashboardToast[]>([]);

  readonly kpis = computed(() => {
    const cases = this.cases();
    return {
      total: cases.length,
      red: cases.filter((patient) => patient.triageCode === 'RED').length,
      yellow: cases.filter((patient) => patient.triageCode === 'YELLOW').length,
      green: cases.filter((patient) => patient.triageCode === 'GREEN').length,
      admitted: cases.filter((patient) =>
        ['1', '3'].includes(patient.currentMovementCategory ?? '')
      ).length,
      discharged: cases.filter((patient) => patient.currentMovementCategory === '2').length,
    };
  });

  readonly filteredCases = computed(() => {
    const query = this.searchText().trim().toLocaleLowerCase();
    const filter = this.triageFilter();
    const fresh = this.newCaseIds();
    const matches = this.cases().filter((patient) => {
      const matchesFilter =
        filter === 'ALL' ||
        patient.triageCode === filter ||
        (filter === 'ADMITTED' && ['1', '3'].includes(patient.currentMovementCategory ?? ''));
      const searchableText = [
        patient.caseId,
        patient.patientId,
        patient.fullName,
        patient.firstName,
        patient.lastName,
        patient.location,
        patient.department,
        patient.mainInjury,
      ]
        .filter(Boolean)
        .join(' ')
        .toLocaleLowerCase();
      return matchesFilter && (!query || searchableText.includes(query));
    });
    // Newly arrived patients float to the top while they are highlighted.
    return fresh.size === 0
      ? matches
      : [...matches.filter((p) => fresh.has(p.caseId)), ...matches.filter((p) => !fresh.has(p.caseId))];
  });

  private readonly subscriptions = new Subscription();
  private clockInterval?: ReturnType<typeof setInterval>;
  private refreshInterval?: ReturnType<typeof setInterval>;
  private pulseTimeout?: ReturnType<typeof setTimeout>;
  private readonly highlightTimers = new Map<string, ReturnType<typeof setTimeout>>();
  private readonly toastTimers = new Map<number, ReturnType<typeof setTimeout>>();
  private latestIncidentNo = '';
  private toastSeq = 0;
  private unseenAlerts = 0;
  private readonly baseTitle = typeof document !== 'undefined' ? document.title : '';
  private casesRequestSeq = 0;

  private readonly onVisibilityChange = (): void => {
    if (document.visibilityState === 'visible') {
      this.resetUnseenAlerts();
      this.refreshInBackground();
    }
  };

  constructor(private readonly aranService: AranService) {}

  ngOnInit(): void {
    this.clockInterval = setInterval(() => this.systemTime.set(new Date()), 1000);
    this.subscriptions.add(
      this.aranService.patientUpdates$.subscribe((update) => this.handlePatientUpdate(update))
    );
    this.subscriptions.add(
      this.aranService.connectionStatus$.subscribe((connected) => this.isConnected.set(connected))
    );
    this.loadInitial();
    this.refreshInterval = setInterval(() => this.refreshInBackground(), BACKGROUND_REFRESH_INTERVAL_MS);
    document.addEventListener('visibilitychange', this.onVisibilityChange);
    void this.aranService.connect().then(
      () => this.isConnected.set(true),
      (error: unknown) => {
        // Background polling keeps the dashboard current even without SignalR.
        this.isConnected.set(false);
        console.warn('ARAN SignalR connection failed; relying on background sync:', error);
      }
    );
  }

  ngOnDestroy(): void {
    this.subscriptions.unsubscribe();
    if (this.clockInterval) clearInterval(this.clockInterval);
    if (this.refreshInterval) clearInterval(this.refreshInterval);
    if (this.pulseTimeout) clearTimeout(this.pulseTimeout);
    this.highlightTimers.forEach((timer) => clearTimeout(timer));
    this.toastTimers.forEach((timer) => clearTimeout(timer));
    document.removeEventListener('visibilitychange', this.onVisibilityChange);
    this.resetUnseenAlerts();
    void this.aranService.disconnect();
  }

  onIncidentChange(accidentNo: string): void {
    if (accidentNo === this.selectedIncident()?.accidentNo) return;
    const incident = this.incidents().find((item) => item.accidentNo === accidentNo) ?? null;
    this.selectIncident(incident);
  }

  refreshNow(): void {
    this.refreshInBackground();
  }

  setFilter(filter: TriageFilter): void {
    this.triageFilter.set(filter);
  }

  openCase(patient: AranPatientCase): void {
    this.activeCase.set(patient);
  }

  closeDrawer(): void {
    this.activeCase.set(null);
  }

  rawCaseJson(patient: AranPatientCase): string {
    return JSON.stringify(patient, null, 2);
  }

  dismissToast(id: number): void {
    const timer = this.toastTimers.get(id);
    if (timer) clearTimeout(timer);
    this.toastTimers.delete(id);
    this.toasts.update((list) => list.filter((toast) => toast.id !== id));
  }

  onToastAction(toast: DashboardToast): void {
    if (toast.accidentNo) this.onIncidentChange(toast.accidentNo);
    this.dismissToast(toast.id);
  }

  trackByCaseId(_: number, patient: AranPatientCase): string {
    return patient.caseId;
  }

  trackByToastId(_: number, toast: DashboardToast): number {
    return toast.id;
  }

  private loadInitial(): void {
    this.isLoading.set(true);
    this.subscriptions.add(
      this.aranService.getLatestIncidents().subscribe({
        next: (payload) => {
          const incidents = payload.incidents ?? [];
          const latest = payload.selectedIncident ?? incidents[0] ?? null;
          this.latestIncidentNo = latest?.accidentNo ?? '';
          this.incidents.set(incidents);
          this.selectedIncident.set(latest);
          this.cases.set(payload.cases ?? []);
          this.isLoading.set(false);
          this.markSynced();
          if (latest) this.joinIncidentGroup(latest.accidentNo);
        },
        error: (error: unknown) => {
          this.isLoading.set(false);
          this.syncFailed.set(true);
          this.errorMessage.set('טעינת אירועי אר״ן נכשלה');
          console.error('Failed to load latest ARAN incidents:', error);
        },
      })
    );
  }

  /** Silently refreshes the incident list and the selected incident's cases, then alerts on changes. */
  private refreshInBackground(): void {
    if (this.isSyncing() || this.isLoading()) return;
    this.isSyncing.set(true);
    const selectedNo = this.selectedIncident()?.accidentNo ?? '';
    const requestSeq = ++this.casesRequestSeq;

    this.subscriptions.add(
      forkJoin({
        payload: this.aranService.getLatestIncidents(),
        cases: selectedNo
          ? this.aranService.getIncidentCases(selectedNo).pipe(catchError(() => of(null)))
          : of(null),
      }).subscribe({
        next: ({ payload, cases }) => {
          this.isSyncing.set(false);
          this.markSynced();
          this.errorMessage.set('');
          this.applyIncidentList(payload);
          // Ignore stale results if the user switched incidents meanwhile.
          if (cases && requestSeq === this.casesRequestSeq && this.selectedIncident()?.accidentNo === selectedNo) {
            this.mergeCases(cases, true);
          }
        },
        error: (error: unknown) => {
          this.isSyncing.set(false);
          this.syncFailed.set(true);
          console.error('ARAN background sync failed:', error);
        },
      })
    );
  }

  private applyIncidentList(payload: AranDashboardPayload): void {
    const previous = new Map(this.incidents().map((item) => [item.accidentNo, item]));
    const incidents = payload.incidents ?? [];
    const latest = payload.selectedIncident ?? incidents[0] ?? null;
    const isNewLatest = !!latest && !!this.latestIncidentNo && latest.accidentNo !== this.latestIncidentNo;
    if (latest) this.latestIncidentNo = latest.accidentNo;
    this.incidents.set(incidents);

    const selectedNo = this.selectedIncident()?.accidentNo;
    if (isNewLatest && latest) {
      this.pushToast({
        kind: 'incident',
        title: 'אירוע אר״ן חדש',
        message: `${latest.accidentNo}${latest.accidentPlace ? ' — ' + latest.accidentPlace : ''} נבחר אוטומטית`,
      });
      this.selectIncident(latest, payload.cases);
      return;
    }

    if (!selectedNo && latest) {
      this.selectIncident(latest, payload.cases);
      return;
    }

    const refreshed = incidents.find((item) => item.accidentNo === selectedNo);
    if (refreshed) this.selectedIncident.set(refreshed);

    // Alert about new patients in incidents the user is not currently viewing.
    for (const incident of incidents) {
      if (incident.accidentNo === selectedNo) continue;
      const before = previous.get(incident.accidentNo)?.patientCount;
      const after = incident.patientCount ?? 0;
      if (before !== undefined && after > before) {
        const added = after - before;
        this.pushToast({
          kind: 'new',
          title: `נפגעים חדשים באירוע ${incident.accidentNo}`,
          message: `${added} ${added === 1 ? 'נפגע חדש' : 'נפגעים חדשים'}${incident.accidentPlace ? ' — ' + incident.accidentPlace : ''}`,
          accidentNo: incident.accidentNo,
          actionLabel: 'הצג',
        });
      }
    }
  }

  private selectIncident(incident: IncidentOption | null, preloadedCases?: AranPatientCase[]): void {
    this.casesRequestSeq++;
    this.selectedIncident.set(incident);
    this.activeCase.set(null);
    this.errorMessage.set('');
    this.clearHighlights();
    if (!incident) {
      this.cases.set([]);
      return;
    }
    this.joinIncidentGroup(incident.accidentNo);
    const isSameIncidentPreload =
      preloadedCases && preloadedCases.every((patient) => patient.accidentNo?.trim() === incident.accidentNo);
    if (preloadedCases && isSameIncidentPreload) {
      this.cases.set(preloadedCases);
    } else {
      this.cases.set([]);
      this.loadCases(incident.accidentNo);
    }
  }

  private loadCases(accidentNo: string): void {
    if (!accidentNo) return;
    const requestSeq = ++this.casesRequestSeq;
    this.isLoading.set(true);
    this.subscriptions.add(
      this.aranService.getIncidentCases(accidentNo).subscribe({
        next: (cases) => {
          if (requestSeq !== this.casesRequestSeq || this.selectedIncident()?.accidentNo !== accidentNo) return;
          this.cases.set(cases);
          this.isLoading.set(false);
          this.markSynced();
        },
        error: (error: unknown) => {
          if (requestSeq !== this.casesRequestSeq) return;
          this.isLoading.set(false);
          this.errorMessage.set('טעינת רשימת הנפגעים נכשלה');
          console.error(`Failed to load cases for incident ${accidentNo}:`, error);
        },
      })
    );
  }

  /** Replaces the table data in place and highlights/alerts on new or changed cases. */
  private mergeCases(cases: AranPatientCase[], notify: boolean): void {
    const previous = new Map(this.cases().map((patient) => [patient.caseId, patient]));
    const added: AranPatientCase[] = [];
    const changed: AranPatientCase[] = [];
    for (const patient of cases) {
      const before = previous.get(patient.caseId);
      if (!before) added.push(patient);
      else if (this.caseFingerprint(before) !== this.caseFingerprint(patient)) changed.push(patient);
    }

    this.cases.set(cases);
    const activeCaseId = this.activeCase()?.caseId;
    if (activeCaseId) {
      this.activeCase.set(cases.find((patient) => patient.caseId === activeCaseId) ?? null);
    }
    if (added.length === 0 && changed.length === 0) return;

    this.highlight(added.map((p) => p.caseId), true);
    this.highlight(changed.map((p) => p.caseId), false);
    this.pulseConnection();
    if (!notify) return;

    const incidentNo = this.selectedIncident()?.accidentNo ?? '';
    if (added.length > 0) {
      const names = added.slice(0, 3).map((p) => this.displayName(p)).join(', ');
      const more = added.length > 3 ? ` ועוד ${added.length - 3}` : '';
      this.pushToast({
        kind: 'new',
        title: added.length === 1 ? `נפגע חדש באירוע ${incidentNo}` : `${added.length} נפגעים חדשים באירוע ${incidentNo}`,
        message: names + more,
      });
    }
    if (changed.length > 0) {
      this.pushToast({
        kind: 'update',
        title: changed.length === 1 ? 'עדכון בתיק נפגע' : `עודכנו ${changed.length} תיקי נפגעים`,
        message: changed.slice(0, 3).map((p) => `${this.displayName(p)}: ${p.currentStatus || p.triageLabel || 'עודכן'}`).join(' · '),
      });
    }
  }

  private handlePatientUpdate(update: AranPatientUpdate): void {
    if (update.accidentNo !== this.selectedIncident()?.accidentNo) return;
    this.isConnected.set(true);
    this.refreshInBackground();
  }

  private caseFingerprint(patient: AranPatientCase): string {
    return [
      patient.triageCode,
      patient.currentStatus,
      patient.currentMovementCategory,
      patient.location,
      patient.department,
      patient.lastMovementTime,
      patient.mainInjury,
      patient.movements?.length ?? 0,
    ].join('|');
  }

  private displayName(patient: AranPatientCase): string {
    const name = patient.fullName || [patient.firstName, patient.lastName].filter(Boolean).join(' ');
    return name || `תיק ${patient.caseId}`;
  }

  private highlight(caseIds: string[], isNew: boolean): void {
    if (caseIds.length === 0) return;
    this.highlightedCaseIds.update((set) => new Set([...set, ...caseIds]));
    if (isNew) this.newCaseIds.update((set) => new Set([...set, ...caseIds]));
    for (const id of caseIds) {
      const existing = this.highlightTimers.get(id);
      if (existing) clearTimeout(existing);
      this.highlightTimers.set(
        id,
        setTimeout(() => {
          this.highlightTimers.delete(id);
          this.highlightedCaseIds.update((set) => this.without(set, id));
          this.newCaseIds.update((set) => this.without(set, id));
        }, ROW_HIGHLIGHT_MS)
      );
    }
  }

  private clearHighlights(): void {
    this.highlightTimers.forEach((timer) => clearTimeout(timer));
    this.highlightTimers.clear();
    this.highlightedCaseIds.set(new Set());
    this.newCaseIds.set(new Set());
  }

  private without(set: Set<string>, id: string): Set<string> {
    const next = new Set(set);
    next.delete(id);
    return next;
  }

  private pushToast(toast: Omit<DashboardToast, 'id'>): void {
    const id = ++this.toastSeq;
    this.toasts.update((list) => [{ ...toast, id }, ...list].slice(0, MAX_TOASTS));
    this.toastTimers.set(id, setTimeout(() => this.dismissToast(id), TOAST_LIFETIME_MS));
    if (toast.kind !== 'error') this.playChime();
    if (typeof document !== 'undefined' && document.visibilityState === 'hidden') {
      this.unseenAlerts++;
      document.title = `(${this.unseenAlerts}) ${this.baseTitle}`;
    }
  }

  private resetUnseenAlerts(): void {
    this.unseenAlerts = 0;
    if (typeof document !== 'undefined') document.title = this.baseTitle;
  }

  private playChime(): void {
    try {
      const AudioCtx = (window as unknown as { AudioContext?: typeof AudioContext }).AudioContext;
      if (!AudioCtx) return;
      const ctx = new AudioCtx();
      const osc = ctx.createOscillator();
      const gain = ctx.createGain();
      osc.type = 'sine';
      osc.frequency.value = 880;
      gain.gain.setValueAtTime(0.0001, ctx.currentTime);
      gain.gain.exponentialRampToValueAtTime(0.15, ctx.currentTime + 0.02);
      gain.gain.exponentialRampToValueAtTime(0.0001, ctx.currentTime + 0.35);
      osc.connect(gain).connect(ctx.destination);
      osc.start();
      osc.stop(ctx.currentTime + 0.4);
      osc.onended = () => void ctx.close();
    } catch {
      // Audio is best-effort (may be blocked until user interaction).
    }
  }

  private pulseConnection(): void {
    this.connectionPulse.set(true);
    if (this.pulseTimeout) clearTimeout(this.pulseTimeout);
    this.pulseTimeout = setTimeout(() => this.connectionPulse.set(false), 1200);
  }

  private markSynced(): void {
    this.lastSyncedAt.set(new Date());
    this.syncFailed.set(false);
  }

  private joinIncidentGroup(accidentNo: string): void {
    void this.aranService.selectIncident(accidentNo).catch((error: unknown) => {
      console.error('Failed to join ARAN incident group:', error);
    });
  }
}
