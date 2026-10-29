import { Component, signal, effect, OnDestroy } from '@angular/core';
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
import { AranHttpService } from './core/services/aran-http.service';
import { AranPollingService } from './core/services/aran-polling.service';
import { ZivAlertToastComponent } from './shared/components/ziv-alert-toast/ziv-alert-toast.component';
import { ZivHeaderComponent } from './shared/components/ziv-header/ziv-header.component';
import { ZivKpiStripComponent } from './shared/components/ziv-kpi-strip/ziv-kpi-strip.component';
import { ZivCasualtyTableComponent } from './shared/components/ziv-casualty-table/ziv-casualty-table.component';

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
    ZivAlertToastComponent,
  ],
  template: `
    <div dir="rtl" class="app-container">
      <app-ziv-alert-toast
        [alerts]="polling.alerts()"
        (dismiss)="polling.dismiss($event)"
      ></app-ziv-alert-toast>

      <!-- Header -->
      <app-ziv-header
        [selectedAccidentNo]="selectedAccidentNo()"
        [isConnected]="polling.isConnected()"
        [systemTime]="systemTime()"
        (accidentNoChange)="onAccidentNoChange($event)"
      ></app-ziv-header>

      <!-- Main content -->
      <main class="main-content">
        <!-- KPI Strip -->
        <app-ziv-kpi-strip
          [kpi]="polling.kpi()"
          [loading]="loading()"
        ></app-ziv-kpi-strip>

        <!-- Casualty Table -->
        <section class="casualty-section">
          <app-ziv-casualty-table
            [casualties]="polling.casualties()"
            [highlightIds]="polling.updatedCaseIds()"
            [loading]="loading()"
            [accidentNo]="selectedAccidentNo()"
            (searchChange)="onSearchChange($event)"
            (syncRequested)="onSyncRequested()"
          ></app-ziv-casualty-table>
        </section>
      </main>

      <!-- Status bar -->
      <footer class="status-footer">
        <span class="status-text" [class.offline]="!polling.isConnected()">
          {{ polling.isSyncing() ? '⟳ מעדכן...' : polling.isConnected() ? '✓ מחובר' : '⚠ מנותק - ניסיון חוזר בעוד דקה' }}
          | עדכון אחרון: {{ polling.lastSync() ? (polling.lastSync() | date: 'HH:mm:ss') : '--:--:--' }}
          | עדכון הבא: {{ polling.nextSync() ? (polling.nextSync() | date: 'HH:mm:ss') : '--:--:--' }}
        </span>
        <button mat-button type="button" (click)="polling.refreshNow()" [disabled]="polling.isSyncing() || !selectedAccidentNo()">רענן עכשיו</button>
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

      .status-text.offline {
        color: #dc2626;
        font-weight: 600;
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
export class AppComponent implements OnDestroy {
  private systemTimeInterval: any;

  selectedAccidentNo = signal<string>('');
  loading = signal<boolean>(false);
  systemTime = signal<Date>(new Date());

  constructor(
    readonly polling: AranPollingService,
    private httpService: AranHttpService
  ) {
    this.systemTimeInterval = setInterval(() => this.systemTime.set(new Date()), 1000);

    // Clear the loading state once the in-flight request completes
    effect(
      () => {
        if (!this.polling.isSyncing()) this.loading.set(false);
      },
      { allowSignalWrites: true }
    );
  }

  ngOnDestroy(): void {
    clearInterval(this.systemTimeInterval);
    this.polling.stop();
  }

  onAccidentNoChange(accidentNo: string): void {
    if (this.selectedAccidentNo() === accidentNo) return;
    this.selectedAccidentNo.set(accidentNo);
    this.loading.set(!!accidentNo);
    this.polling.start(accidentNo);
  }

  onSearchChange(searchTerm: string): void {
    this.polling.setSearch(searchTerm);
  }

  onSyncRequested(): void {
    const accidentNo = this.selectedAccidentNo();
    if (!accidentNo) return;

    this.loading.set(true);
    this.httpService.syncAran({ accidentNo }).subscribe({
      next: () => this.polling.refreshNow(),
      error: (err) => {
        console.error('Sync request failed:', err);
        this.loading.set(false);
      },
    });
  }
}
