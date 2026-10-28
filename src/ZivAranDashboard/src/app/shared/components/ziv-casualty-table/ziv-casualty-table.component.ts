import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatCardModule } from '@angular/material/card';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatChipsModule } from '@angular/material/chips';
import { Casualty } from '../../../core/models/aran.models';

@Component({
  selector: 'app-ziv-casualty-table',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatTableModule,
    MatButtonModule,
    MatIconModule,
    MatFormFieldModule,
    MatInputModule,
    MatCardModule,
    MatProgressSpinnerModule,
    MatChipsModule,
  ],
  template: `
    <mat-card>
      <div class="table-header">
        <h2>נפגעים פעילים</h2>
        <div class="table-controls">
          <mat-form-field appearance="fill">
            <mat-label>חיפוש</mat-label>
            <input
              matInput
              placeholder="מס׳ מקרה, מטופל, מיקום"
              (input)="onSearchInput($event)"
            />
            <mat-icon matSuffix>search</mat-icon>
          </mat-form-field>
          <button mat-raised-button color="primary" (click)="onSyncClick()">
            <mat-icon>sync</mat-icon>
            סנכרן
          </button>
        </div>
      </div>

      <div *ngIf="loading" class="loading-spinner">
        <mat-spinner diameter="40"></mat-spinner>
      </div>

      <div *ngIf="!loading && casualties.length === 0" class="no-data">
        אין נפגעים פעילים
      </div>

      <table *ngIf="!loading && casualties.length > 0" mat-table [dataSource]="casualties" dir="rtl">
        <!-- Case Number -->
        <ng-container matColumnDef="caseNumber">
          <th mat-header-cell *matHeaderCellDef>מס׳ מקרה</th>
          <td mat-cell *matCellDef="let element" class="monospace">{{ element.caseNumber }}</td>
        </ng-container>

        <!-- Patient ID -->
        <ng-container matColumnDef="patientID">
          <th mat-header-cell *matHeaderCellDef>מס׳ זהות</th>
          <td mat-cell *matCellDef="let element">{{ element.patientID || '-' }}</td>
        </ng-container>

        <!-- Current Movement -->
        <ng-container matColumnDef="currentMovement">
          <th mat-header-cell *matHeaderCellDef>מצב נוכחי</th>
          <td mat-cell *matCellDef="let element">
            <span class="movement-badge">{{ element.currentMovement || '-' }}</span>
          </td>
        </ng-container>

        <!-- Triage Status -->
        <ng-container matColumnDef="triageStatus">
          <th mat-header-cell *matHeaderCellDef>עדיפות</th>
          <td mat-cell *matCellDef="let element">
            <mat-chip
              [class.red]="element.triageStatus === 'RED'"
              [class.yellow]="element.triageStatus === 'YELLOW'"
              [class.green]="element.triageStatus === 'GREEN'"
              [class.black]="element.triageStatus === 'BLACK'"
            >
              {{ element.triageStatus || '-' }}
            </mat-chip>
          </td>
        </ng-container>

        <!-- Location -->
        <ng-container matColumnDef="currentLocation">
          <th mat-header-cell *matHeaderCellDef>מיקום</th>
          <td mat-cell *matCellDef="let element">{{ element.currentLocation || '-' }}</td>
        </ng-container>

        <!-- Last Update -->
        <ng-container matColumnDef="lastMovementTime">
          <th mat-header-cell *matHeaderCellDef>עדכון אחרון</th>
          <td mat-cell *matCellDef="let element">
            {{ element.lastMovementTime | date: 'HH:mm:ss' }}
          </td>
        </ng-container>

        <!-- Actions -->
        <ng-container matColumnDef="actions">
          <th mat-header-cell *matHeaderCellDef>פעולות</th>
          <td mat-cell *matCellDef="let element">
            <button mat-icon-button matTooltip="פרטים">
              <mat-icon>info</mat-icon>
            </button>
          </td>
        </ng-container>

        <tr mat-header-row *matHeaderRowDef="displayedColumns"></tr>
        <tr mat-row *matRowDef="let row; columns: displayedColumns;"></tr>
      </table>
    </mat-card>
  `,
  styles: [
    `
      mat-card {
        border: 1px solid #e2e8f0;
        border-radius: 4px;
        background-color: #ffffff;
      }

      .table-header {
        padding: 16px;
        border-bottom: 1px solid #e2e8f0;
        display: flex;
        justify-content: space-between;
        align-items: center;
      }

      .table-header h2 {
        margin: 0;
        font-size: 18px;
        font-weight: 600;
        color: #0f172a;
      }

      .table-controls {
        display: flex;
        gap: 12px;
        align-items: center;
      }

      .loading-spinner {
        display: flex;
        justify-content: center;
        padding: 32px;
      }

      .no-data {
        text-align: center;
        padding: 32px;
        color: #64748b;
      }

      table {
        width: 100%;
      }

      th {
        font-weight: 600;
        color: #0f172a;
        background-color: #f8fafc;
        text-align: right;
      }

      td {
        padding: 12px;
        border-bottom: 1px solid #f1f5f9;
        text-align: right;
      }

      tr:hover {
        background-color: #f8fafc;
      }

      .monospace {
        font-family: 'Courier New', monospace;
        font-weight: 600;
      }

      .movement-badge {
        display: inline-block;
        background-color: #f1f5f9;
        padding: 4px 8px;
        border-radius: 2px;
        font-size: 12px;
        color: #0f172a;
      }

      mat-chip {
        font-size: 12px !important;
        height: 24px !important;
      }

      mat-chip.red {
        background-color: #dc2626 !important;
        color: #ffffff !important;
      }

      mat-chip.yellow {
        background-color: #d97706 !important;
        color: #ffffff !important;
      }

      mat-chip.green {
        background-color: #16a34a !important;
        color: #ffffff !important;
      }

      mat-chip.black {
        background-color: #1f2937 !important;
        color: #ffffff !important;
      }

      button[mat-raised-button] {
        text-transform: none;
        font-weight: 500;
      }

      @media (max-width: 1200px) {
        .table-header {
          flex-direction: column;
          gap: 12px;
        }
      }
    `,
  ],
})
export class ZivCasualtyTableComponent {
  @Input() casualties: Casualty[] = [];
  @Input() loading: boolean = false;
  @Input() accidentNo: string = '';
  @Output() searchChange = new EventEmitter<string>();
  @Output() syncRequested = new EventEmitter<void>();

  displayedColumns: string[] = [
    'caseNumber',
    'patientID',
    'currentMovement',
    'triageStatus',
    'currentLocation',
    'lastMovementTime',
    'actions',
  ];

  onSearchInput(event: Event): void {
    const searchTerm = (event.target as HTMLInputElement).value;
    this.searchChange.emit(searchTerm);
  }

  onSyncClick(): void {
    this.syncRequested.emit();
  }
}
