import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';
import { AranAlert } from '../../../core/services/aran-polling.service';

@Component({
  selector: 'app-ziv-alert-toast',
  standalone: true,
  imports: [CommonModule, MatIconModule],
  template: `
    <div class="toast-stack" aria-live="polite" dir="rtl">
      <div
        *ngFor="let a of alerts; trackBy: trackById"
        class="toast"
        [ngClass]="a.type"
        [attr.role]="a.type === 'critical' ? 'alert' : 'status'"
      >
        <mat-icon class="toast-icon">{{ a.type === 'critical' ? 'warning' : a.type === 'new' ? 'person_add' : 'sync' }}</mat-icon>
        <div class="toast-body">
          <strong>{{ a.title }}</strong>
          <span>{{ a.message }}</span>
          <small>{{ a.timestamp | date: 'HH:mm:ss' }}</small>
        </div>
        <button type="button" class="toast-close" (click)="dismiss.emit(a.id)" aria-label="סגור התראה">✕</button>
        <div class="toast-progress" *ngIf="a.type !== 'critical'"></div>
      </div>
    </div>
  `,
  styles: [
    `
      .toast-stack {
        position: fixed;
        top: 16px;
        left: 16px;
        z-index: 1000;
        display: flex;
        flex-direction: column;
        gap: 10px;
        width: min(360px, calc(100vw - 32px));
        pointer-events: none;
      }
      .toast {
        position: relative;
        overflow: hidden;
        pointer-events: auto;
        display: flex;
        align-items: flex-start;
        gap: 12px;
        padding: 12px 14px;
        background: #fff;
        border-radius: 12px;
        border-inline-start: 5px solid #3b82f6;
        box-shadow: 0 10px 25px rgba(15, 23, 42, 0.18);
        animation: toastIn 0.3s cubic-bezier(0.2, 0.9, 0.3, 1.2);
      }
      .toast.new { border-inline-start-color: #16a34a; }
      .toast.critical {
        border-inline-start-color: #dc2626;
        background: #fef2f2;
        animation: toastIn 0.3s ease-out, pulse 1.6s ease-in-out 3;
      }
      .toast-icon { color: #3b82f6; flex-shrink: 0; }
      .toast.new .toast-icon { color: #16a34a; }
      .toast.critical .toast-icon { color: #dc2626; }
      .toast-body { display: flex; flex-direction: column; gap: 2px; flex: 1; font-size: 14px; color: #0f172a; }
      .toast-body span { color: #475569; }
      .toast-body small { color: #94a3b8; font-size: 11px; }
      .toast-close { background: none; border: 0; cursor: pointer; color: #64748b; font-size: 14px; padding: 2px 4px; }
      .toast-close:hover { color: #0f172a; }
      .toast-progress {
        position: absolute; bottom: 0; right: 0; height: 3px; width: 100%;
        background: currentColor; color: #93c5fd; transform-origin: right;
        animation: shrink 8s linear forwards;
      }
      .toast.new .toast-progress { color: #86efac; }
      @keyframes shrink { to { transform: scaleX(0); } }
      @keyframes toastIn { from { opacity: 0; transform: translateX(-24px); } to { opacity: 1; transform: none; } }
      @keyframes pulse { 50% { box-shadow: 0 0 0 6px rgba(220, 38, 38, 0.25); } }
      @media (prefers-reduced-motion: reduce) { .toast, .toast-progress { animation: none; } }
    `,
  ],
})
export class ZivAlertToastComponent {
  @Input() alerts: AranAlert[] = [];
  @Output() dismiss = new EventEmitter<string>();
  trackById = (_: number, a: AranAlert) => a.id;
}
