import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { KpiSummary } from '../../../core/models/aran.models';

@Component({
  selector: 'app-ziv-kpi-strip',
  standalone: true,
  imports: [CommonModule, MatCardModule, MatProgressBarModule],
  template: `
    <div class="kpi-container">
      <mat-card class="kpi-card" [class.loading]="loading">
        <div class="kpi-header" style="border-top: 3px solid #1e4e9d">
          <span class="kpi-value">{{ kpi?.total || 0 }}</span>
          <span class="kpi-label">סה״כ נקלטו בזיו</span>
        </div>
      </mat-card>

      <mat-card class="kpi-card" [class.loading]="loading">
        <div class="kpi-header" style="border-top: 3px solid #dc2626">
          <span class="kpi-value" style="color: #dc2626">{{ kpi?.red || 0 }}</span>
          <span class="kpi-label">קשה / דחוף T1</span>
        </div>
      </mat-card>

      <mat-card class="kpi-card" [class.loading]="loading">
        <div class="kpi-header" style="border-top: 3px solid #d97706">
          <span class="kpi-value" style="color: #d97706">{{ kpi?.yellow || 0 }}</span>
          <span class="kpi-label">בינוני T2</span>
        </div>
      </mat-card>

      <mat-card class="kpi-card" [class.loading]="loading">
        <div class="kpi-header" style="border-top: 3px solid #16a34a">
          <span class="kpi-value" style="color: #16a34a">{{ kpi?.green || 0 }}</span>
          <span class="kpi-label">קל T3</span>
        </div>
      </mat-card>

      <mat-card class="kpi-card" [class.loading]="loading">
        <div class="kpi-header" style="border-top: 3px solid #0284c7">
          <span class="kpi-value" style="color: #0284c7">{{ kpi?.inTreatment || 0 }}</span>
          <span class="kpi-label">באשפוז / חדר ניתוח</span>
        </div>
      </mat-card>

      <mat-card class="kpi-card" [class.loading]="loading">
        <div class="kpi-header" style="border-top: 3px solid #64748b">
          <span class="kpi-value" style="color: #64748b">{{ kpi?.discharged || 0 }}</span>
          <span class="kpi-label">שוחררו / שונות</span>
        </div>
      </mat-card>
    </div>
  `,
  styles: [
    `
      .kpi-container {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
        gap: 12px;
        width: 100%;
      }

      .kpi-card {
        min-height: 100px;
        display: flex;
        align-items: center;
        justify-content: center;
        border: 1px solid #e2e8f0;
        border-radius: 4px;
        background-color: #ffffff;
        padding: 16px;
      }

      .kpi-card.loading {
        opacity: 0.6;
      }

      .kpi-header {
        display: flex;
        flex-direction: column;
        align-items: center;
        justify-content: center;
        gap: 8px;
        width: 100%;
      }

      .kpi-value {
        font-size: 28px;
        font-weight: 700;
        color: #0f172a;
      }

      .kpi-label {
        font-size: 12px;
        font-weight: 500;
        color: #64748b;
        text-align: center;
      }
    `,
  ],
})
export class ZivKpiStripComponent {
  @Input() kpi: KpiSummary | null = null;
  @Input() loading: boolean = false;
}
