import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatSelectModule } from '@angular/material/select';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatIconModule } from '@angular/material/icon';
import { MatBadgeModule } from '@angular/material/badge';
import { MatButtonModule } from '@angular/material/button';
import { FormsModule } from '@angular/forms';

@Component({
  selector: 'app-ziv-header',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatToolbarModule,
    MatSelectModule,
    MatFormFieldModule,
    MatInputModule,
    MatIconModule,
    MatBadgeModule,
    MatButtonModule,
  ],
  template: `
    <mat-toolbar color="primary" dir="rtl">
      <div class="toolbar-content">
        <div class="logo-section">
          <div class="logo-badge">
            <span class="logo-text">זיו</span>
          </div>
          <span class="hospital-name">המרכז הרפואי זיו — חמ״ל אר״ן וניטור נפגעים מרכזי</span>
        </div>

        <div class="controls-section">
          <div class="incident-selector">
            <mat-form-field appearance="fill">
              <mat-label>בחר אירוע</mat-label>
              <input
                matInput
                placeholder="הזן מס׳ אירוע"
                [(ngModel)]="accidentNo"
                (change)="onAccidentNoChange()"
              />
            </mat-form-field>
          </div>

          <div class="status-badges">
            <button
              mat-icon-button
              [matBadge]="isConnected ? '' : '!'"
              matBadgeColor="warn"
              [attr.aria-label]="isConnected ? 'מחובר' : 'מנותק'"
            >
              <mat-icon [class.connected]="isConnected">
                {{ isConnected ? 'cloud_done' : 'cloud_off' }}
              </mat-icon>
            </button>

            <div class="system-time">
              {{ systemTime | date: 'HH:mm:ss' }}
            </div>
          </div>
        </div>
      </div>
    </mat-toolbar>
  `,
  styles: [
    `
      mat-toolbar {
        background-color: #ffffff !important;
        border-bottom: 1px solid #e2e8f0;
      }

      .toolbar-content {
        display: flex;
        justify-content: space-between;
        align-items: center;
        width: 100%;
        gap: 16px;
        padding: 8px 16px;
      }

      .logo-section {
        display: flex;
        align-items: center;
        gap: 16px;
        flex: 1;
      }

      .logo-badge {
        width: 48px;
        height: 48px;
        border-radius: 4px;
        background: linear-gradient(135deg, #1e4e9d 0%, #f15a24 100%);
        display: flex;
        align-items: center;
        justify-content: center;
        flex-shrink: 0;
      }

      .logo-text {
        color: #ffffff;
        font-weight: 700;
        font-size: 24px;
      }

      .hospital-name {
        font-size: 13px;
        font-weight: 600;
        color: #0f172a;
        white-space: nowrap;
      }

      .controls-section {
        display: flex;
        align-items: center;
        gap: 24px;
      }

      .incident-selector {
        min-width: 250px;
      }

      .status-badges {
        display: flex;
        align-items: center;
        gap: 12px;
      }

      .system-time {
        font-size: 12px;
        font-weight: 600;
        color: #64748b;
        font-family: 'Courier New', monospace;
      }

      mat-icon.connected {
        color: #16a34a;
      }

      @media (max-width: 768px) {
        .hospital-name {
          display: none;
        }

        .incident-selector {
          min-width: 180px;
        }
      }
    `,
  ],
})
export class ZivHeaderComponent {
  @Input() selectedAccidentNo: string = '';
  @Input() isConnected: boolean = false;
  @Input() systemTime: Date = new Date();
  @Output() accidentNoChange = new EventEmitter<string>();

  accidentNo: string = '';

  ngOnChanges(): void {
    this.accidentNo = this.selectedAccidentNo;
  }

  onAccidentNoChange(): void {
    this.accidentNoChange.emit(this.accidentNo);
  }
}
