import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { AranService } from './core/services/aran.service';
import {
  AranPatientCase,
  AranPatientUpdate,
  IncidentOption,
} from './core/models/aran.models';
import { ZivHeaderComponent } from './shared/components/ziv-header/ziv-header.component';

type TriageFilter = 'ALL' | 'RED' | 'YELLOW' | 'GREEN' | 'ADMITTED';

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
  readonly isConnected = signal(false);
  readonly connectionPulse = signal(false);
  readonly errorMessage = signal('');
  readonly highlightedCaseIds = signal<Set<string>>(new Set());

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
    return this.cases().filter((patient) => {
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
  });

  private readonly subscriptions = new Subscription();
  private clockInterval?: ReturnType<typeof setInterval>;
  private highlightTimeout?: ReturnType<typeof setTimeout>;
  private pulseTimeout?: ReturnType<typeof setTimeout>;

  constructor(private readonly aranService: AranService) {}

  ngOnInit(): void {
    this.clockInterval = setInterval(() => this.systemTime.set(new Date()), 1000);
    this.subscriptions.add(
      this.aranService.patientUpdates$.subscribe((update) => this.handlePatientUpdate(update))
    );
    this.subscriptions.add(
      this.aranService.connectionStatus$.subscribe((connected) => this.isConnected.set(connected))
    );
    this.loadLatestIncidents();
    void this.aranService.connect().then(
      () => this.isConnected.set(true),
      (error: unknown) => {
        this.isConnected.set(false);
        this.errorMessage.set('לא ניתן להתחבר לעדכונים בזמן אמת');
        console.error('ARAN SignalR connection failed:', error);
      }
    );
  }

  ngOnDestroy(): void {
    this.subscriptions.unsubscribe();
    if (this.clockInterval) clearInterval(this.clockInterval);
    if (this.highlightTimeout) clearTimeout(this.highlightTimeout);
    if (this.pulseTimeout) clearTimeout(this.pulseTimeout);
    void this.aranService.disconnect();
  }

  onIncidentChange(accidentNo: string): void {
    const incident = this.incidents().find((item) => item.accidentNo === accidentNo) ?? null;
    this.selectedIncident.set(incident);
    this.cases.set([]);
    this.activeCase.set(null);
    this.errorMessage.set('');
    void this.aranService.selectIncident(accidentNo).catch((error: unknown) => {
      console.error('Failed to join ARAN incident group:', error);
    });
    this.loadCases(accidentNo);
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

  private loadLatestIncidents(): void {
    this.isLoading.set(true);
    this.subscriptions.add(
      this.aranService.getLatestIncidents().subscribe({
        next: (payload) => {
          this.incidents.set(payload.incidents ?? []);
          this.selectedIncident.set(payload.selectedIncident);
          this.cases.set(payload.cases ?? []);
          this.isLoading.set(false);
          if (payload.selectedIncident) {
            void this.aranService.selectIncident(payload.selectedIncident.accidentNo).catch((error: unknown) => {
              console.error('Failed to join latest ARAN incident group:', error);
            });
          }
        },
        error: (error: unknown) => {
          this.isLoading.set(false);
          this.errorMessage.set('טעינת אירועי אר״ן נכשלה');
          console.error('Failed to load latest ARAN incidents:', error);
        },
      })
    );
  }

  private loadCases(accidentNo: string): void {
    if (!accidentNo) return;
    this.isLoading.set(true);
    this.subscriptions.add(
      this.aranService.getIncidentCases(accidentNo).subscribe({
        next: (cases) => {
          if (this.selectedIncident()?.accidentNo !== accidentNo) return;
          this.cases.set(cases);
          const activeCaseId = this.activeCase()?.caseId;
          if (activeCaseId) {
            this.activeCase.set(cases.find((patient) => patient.caseId === activeCaseId) ?? null);
          }
          this.isLoading.set(false);
        },
        error: (error: unknown) => {
          this.isLoading.set(false);
          this.errorMessage.set('טעינת רשימת הנפגעים נכשלה');
          console.error(`Failed to load cases for incident ${accidentNo}:`, error);
        },
      })
    );
  }

  private handlePatientUpdate(update: AranPatientUpdate): void {
    if (update.accidentNo !== this.selectedIncident()?.accidentNo) return;
    this.isConnected.set(true);
    this.connectionPulse.set(true);
    this.highlightedCaseIds.set(new Set([update.caseId]));
    if (this.highlightTimeout) clearTimeout(this.highlightTimeout);
    if (this.pulseTimeout) clearTimeout(this.pulseTimeout);
    this.highlightTimeout = setTimeout(() => this.highlightedCaseIds.set(new Set()), 5000);
    this.pulseTimeout = setTimeout(() => this.connectionPulse.set(false), 1200);
    this.loadCases(update.accidentNo);
  }
}
