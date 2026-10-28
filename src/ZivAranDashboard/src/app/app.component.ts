import { Component, signal, computed, effect, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule, ReactiveFormsModule } from '@angular/forms';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { MatSelectModule } from '@angular/material/select';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatTableModule } from '@angular/material/table';
import { MatPaginatorModule } from '@angular/material/paginator';
import { MatSortModule } from '@angular/material/sort';
import { MatCardModule } from '@angular/material/card';
import { MatBadgeModule } from '@angular/material/badge';
import { MatIconModule } from '@angular/material/icon';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatListModule } from '@angular/material/list';
import { MatDividerModule } from '@angular/material/divider';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialogModule } from '@angular/material/dialog';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { AranSignalRService } from './core/services/aran-signalr.service';
import { AranHttpService } from './core/services/aran-http.service';
import { ZivHeaderComponent } from './shared/components/ziv-header/ziv-header.component';
import { ZivKpiStripComponent } from './shared/components/ziv-kpi-strip/ziv-kpi-strip.component';
import { ZivCasualtyTableComponent } from './shared/components/ziv-casualty-table/ziv-casualty-table.component';
import { Casualty, KpiSummary } from './core/models/aran.models';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    ReactiveFormsModule,
    MatToolbarModule,
    MatButtonModule,
    MatSelectModule,
    MatFormFieldModule,
    MatInputModule,
    MatTableModule,
    MatPaginatorModule,
    MatSortModule,
    MatCardModule,
    MatBadgeModule,
    MatIconModule,
    MatSidenavModule,
    MatListModule,
    MatDividerModule,
    MatChipsModule,
    MatDialogModule,
    MatProgressSpinnerModule,
    ZivHeaderComponent,
    ZivKpiStripComponent,
    ZivCasualtyTableComponent,
  ],
  template: `
    <div dir="rtl" class="app-container">
      <!-- Header -->
      <app-ziv-header
        [selectedAccidentNo]="selectedAccidentNo()"
        [isConnected]="isConnected()"
        [systemTime]="systemTime()"
        (accidentNoChange)="onAccidentNoChange($event)"
      ></app-ziv-header>

      <!-- Main content -->
      <main class="main-content">
        <!-- KPI Strip -->
        <app-ziv-kpi-strip
          [kpi]="kpi()"
          [loading]="loading()"
        ></app-ziv-kpi-strip>

        <!-- Casualty Table -->
        <section class="casualty-section">
          <app-ziv-casualty-table
            [casualties]="casualties()"
            [loading]="loading()"
            [accidentNo]="selectedAccidentNo()"
            (searchChange)="onSearchChange($event)"
            (syncRequested)="onSyncRequested()"
          ></app-ziv-casualty-table>
        </section>
      </main>

      <!-- Status bar -->
      <footer class="status-footer">
        <span class="status-text">
          {{ isConnected() ? '✓ מחובר' : '⚠ מנותק' }}
          | אחרון עדכון: {{ lastUpdateTime() | date: 'HH:mm:ss' }}
        </span>
      </footer>
    </div>
  `,
  styles: [
    `
      .app-container {
        display: flex;
        flex-direction: column;
        height: 100vh;
        background-color: #f8fafc;
        font-family: 'Rubik', 'Assistant', sans-serif;
        overflow: hidden;
      }

      .main-content {
        flex: 1;
        overflow-y: auto;
        padding: 16px;
        display: flex;
        flex-direction: column;
        gap: 16px;
      }

      .casualty-section {
        flex: 1;
        overflow-y: auto;
      }

      .status-footer {
        background-color: #ffffff;
        border-top: 1px solid #e2e8f0;
        padding: 8px 16px;
        text-align: center;
        font-size: 12px;
        color: #64748b;
      }

      .status-text {
        margin: 0 8px;
      }

      @media (prefers-color-scheme: light) {
        .app-container {
          background-color: #f8fafc;
          color: #0f172a;
        }
      }
    `,
  ],
})
export class AppComponent implements OnInit, OnDestroy {
  private destroy$ = new Subject<void>();
  private systemTimeInterval: any;

  // Signals
  selectedAccidentNo = signal<string>('');
  casualties = signal<Casualty[]>([]);
  kpi = signal<KpiSummary | null>(null);
  isConnected = signal<boolean>(false);
  loading = signal<boolean>(false);
  systemTime = signal<Date>(new Date());
  lastUpdateTime = signal<Date>(new Date());

  constructor(
    private signalRService: AranSignalRService,
    private httpService: AranHttpService
  ) {
    // Update system time every second
    this.systemTimeInterval = setInterval(() => {
      this.systemTime.set(new Date());
    }, 1000);
  }

  ngOnInit(): void {
    // Initialize SignalR connection
    this.signalRService
      .connect()
      .then(() => {
        console.log('SignalR connected');
      })
      .catch((err) => {
        console.error('Failed to connect SignalR:', err);
      });

    // Listen to connection status
    this.signalRService
      .isConnected()
      .pipe(takeUntil(this.destroy$))
      .subscribe((connected) => {
        this.isConnected.set(connected);
      });

    // Listen to casualty updates
    this.signalRService
      .getCasualtyUpdates()
      .pipe(takeUntil(this.destroy$))
      .subscribe((update) => {
        if (update && update.accidentNo === this.selectedAccidentNo()) {
          this.casualties.set(update.casualties);
          this.lastUpdateTime.set(update.updatedAt);
        }
      });

    // Listen to KPI updates
    this.signalRService
      .getKpiUpdates()
      .pipe(takeUntil(this.destroy$))
      .subscribe((update) => {
        if (update && update.accidentNo === this.selectedAccidentNo()) {
          this.kpi.set(update.kpi);
        }
      });

    // Listen to system events
    this.signalRService
      .getSystemEvents()
      .pipe(takeUntil(this.destroy$))
      .subscribe((event) => {
        if (event) {
          console.log('System event:', event);
        }
      });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
    if (this.systemTimeInterval) {
      clearInterval(this.systemTimeInterval);
    }
    this.signalRService.disconnect();
  }

  onAccidentNoChange(accidentNo: string): void {
    if (this.selectedAccidentNo() !== accidentNo) {
      if (this.selectedAccidentNo()) {
        this.signalRService
          .leaveIncidentGroup(this.selectedAccidentNo())
          .catch((err) => console.error('Failed to leave group:', err));
      }

      this.selectedAccidentNo.set(accidentNo);

      if (accidentNo) {
        this.loading.set(true);
        this.signalRService
          .joinIncidentGroup(accidentNo)
          .then(() => {
            return Promise.all([
              this.httpService.getCases(accidentNo).toPromise(),
              this.httpService.getKpis(accidentNo).toPromise(),
            ]);
          })
          .then(([casualties, kpi]) => {
            if (casualties) this.casualties.set(casualties);
            if (kpi) this.kpi.set(kpi);
            this.lastUpdateTime.set(new Date());
          })
          .catch((err) => {
            console.error('Failed to load incident data:', err);
          })
          .finally(() => {
            this.loading.set(false);
          });
      }
    }
  }

  onSearchChange(searchTerm: string): void {
    if (!this.selectedAccidentNo()) return;

    this.loading.set(true);
    this.httpService
      .getCases(this.selectedAccidentNo(), searchTerm)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (casualties) => {
          this.casualties.set(casualties);
          this.loading.set(false);
        },
        error: (err) => {
          console.error('Search failed:', err);
          this.loading.set(false);
        },
      });
  }

  onSyncRequested(): void {
    if (!this.selectedAccidentNo()) return;

    this.loading.set(true);
    this.httpService
      .syncAran({ accidentNo: this.selectedAccidentNo() })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (result) => {
          console.log('Sync completed:', result);
          if (result.success) {
            // Data will be updated via SignalR
          } else {
            console.error('Sync failed:', result.errorMessage);
          }
          this.loading.set(false);
        },
        error: (err) => {
          console.error('Sync request failed:', err);
          this.loading.set(false);
        },
      });
  }
}
