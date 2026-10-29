import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { trigger, transition, style, animate } from '@angular/animations';
import { Subject } from 'rxjs';
import { takeUntil, debounceTime } from 'rxjs/operators';
import { AranPollingService, DataChangeNotification, PollingState } from '../../services/aran-polling.service';

interface NotificationAlert {
  id: string;
  notification: DataChangeNotification;
  isShowing: boolean;
  timeRemaining: number;
  icon: string;
  color: string;
}

@Component({
  selector: 'app-data-notification-panel',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="notification-container">
      <!-- Main Alert Cards -->
      <div class="alerts-wrapper">
        <div
          *ngFor="let alert of activeAlerts"
          @slideIn
          class="alert-card"
          [ngClass]="alert.color"
        >
          <div class="alert-header">
            <span class="alert-icon">{{ alert.icon }}</span>
            <div class="alert-content">
              <h4 class="alert-title">{{ alert.notification.type === 'cases' ? 'Casualty Update' : 'KPI Update' }}</h4>
              <p class="alert-message">{{ alert.notification.message }}</p>
              <p class="alert-details">Incident: {{ alert.notification.affectedAccidentNo }}</p>
            </div>
            <button class="close-btn" (click)="dismissAlert(alert.id)" aria-label="Close alert">
              ✕
            </button>
          </div>
          <div class="alert-footer">
            <span class="time-label">{{ alert.notification.timestamp | date:'HH:mm:ss' }}</span>
            <div class="progress-bar">
              <div
                class="progress-fill"
                [style.width.%]="(alert.timeRemaining / 5000) * 100"
              ></div>
            </div>
          </div>
        </div>
      </div>

      <!-- Polling Status Bar -->
      <div class="polling-status-bar" *ngIf="pollingState$ | async as state">
        <div class="status-indicator" [ngClass]="{ active: state.isPolling }">
          <span class="pulse-dot"></span>
          <span class="status-text">
            {{ state.isPolling ? 'Live Polling' : 'Polling Inactive' }}
          </span>
        </div>

        <div class="status-details">
          <span class="detail-item">
            Polls: <strong>{{ state.pollCount }}</strong>
          </span>
          <span class="detail-item" *ngIf="state.lastPollTime">
            Last: <strong>{{ getTimeSince(state.lastPollTime) }}</strong>
          </span>
          <span class="detail-item countdown" *ngIf="state.isPolling">
            Next: <strong>{{ state.nextPollIn }}s</strong>
          </span>
          <span class="detail-item error" *ngIf="state.error">
            Error: {{ state.error }}
          </span>
        </div>
      </div>

      <!-- Toast Notifications (stacked on top-right) -->
      <div class="toast-container">
        <div
          *ngFor="let toast of toastNotifications"
          @toastSlide
          class="toast"
          [ngClass]="'toast-' + toast.notification.type"
        >
          <span class="toast-icon">{{ toast.icon }}</span>
          <span class="toast-text">{{ toast.notification.message }}</span>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .notification-container {
      position: fixed;
      top: 20px;
      right: 20px;
      z-index: 9999;
      font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Oxygen, Ubuntu, Cantarell, sans-serif;
    }

    /* Alert Cards */
    .alerts-wrapper {
      display: flex;
      flex-direction: column;
      gap: 12px;
      max-width: 450px;
      max-height: 60vh;
      overflow-y: auto;
      padding-right: 8px;
    }

    .alerts-wrapper::-webkit-scrollbar {
      width: 6px;
    }

    .alerts-wrapper::-webkit-scrollbar-track {
      background: transparent;
    }

    .alerts-wrapper::-webkit-scrollbar-thumb {
      background: rgba(0, 0, 0, 0.2);
      border-radius: 3px;
    }

    .alert-card {
      background: white;
      border-radius: 8px;
      padding: 16px;
      box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
      border-left: 4px solid;
      overflow: hidden;
    }

    .alert-card.success {
      border-left-color: #10b981;
      background: rgba(16, 185, 129, 0.05);
    }

    .alert-card.warning {
      border-left-color: #f59e0b;
      background: rgba(245, 158, 11, 0.05);
    }

    .alert-card.info {
      border-left-color: #3b82f6;
      background: rgba(59, 130, 246, 0.05);
    }

    .alert-header {
      display: flex;
      gap: 12px;
      margin-bottom: 12px;
    }

    .alert-icon {
      font-size: 24px;
      min-width: 32px;
      text-align: center;
    }

    .alert-content {
      flex: 1;
    }

    .alert-title {
      margin: 0 0 4px 0;
      font-size: 14px;
      font-weight: 600;
      color: #1f2937;
    }

    .alert-message {
      margin: 0 0 4px 0;
      font-size: 13px;
      color: #4b5563;
    }

    .alert-details {
      margin: 0;
      font-size: 12px;
      color: #9ca3af;
    }

    .close-btn {
      background: none;
      border: none;
      font-size: 18px;
      color: #d1d5db;
      cursor: pointer;
      padding: 0;
      width: 24px;
      height: 24px;
      display: flex;
      align-items: center;
      justify-content: center;
      border-radius: 4px;
      transition: all 0.2s;
    }

    .close-btn:hover {
      background: rgba(0, 0, 0, 0.05);
      color: #6b7280;
    }

    .alert-footer {
      display: flex;
      justify-content: space-between;
      align-items: center;
      font-size: 11px;
      color: #9ca3af;
    }

    .progress-bar {
      height: 2px;
      background: rgba(0, 0, 0, 0.1);
      border-radius: 2px;
      overflow: hidden;
      width: 60px;
    }

    .progress-fill {
      height: 100%;
      background: linear-gradient(90deg, #10b981, #3b82f6);
      transition: width 0.1s linear;
    }

    /* Polling Status Bar */
    .polling-status-bar {
      background: white;
      border: 1px solid #e5e7eb;
      border-radius: 8px;
      padding: 12px 16px;
      margin-top: 12px;
      font-size: 12px;
      box-shadow: 0 2px 8px rgba(0, 0, 0, 0.1);
      max-width: 450px;
    }

    .status-indicator {
      display: flex;
      align-items: center;
      gap: 8px;
      margin-bottom: 8px;
      font-weight: 500;
      color: #6b7280;
    }

    .status-indicator.active {
      color: #10b981;
    }

    .pulse-dot {
      width: 8px;
      height: 8px;
      border-radius: 50%;
      background: #9ca3af;
    }

    .status-indicator.active .pulse-dot {
      background: #10b981;
      animation: pulse 2s cubic-bezier(0.4, 0, 0.6, 1) infinite;
    }

    @keyframes pulse {
      0%, 100% { opacity: 1; }
      50% { opacity: 0.5; }
    }

    .status-details {
      display: flex;
      gap: 16px;
      flex-wrap: wrap;
      align-items: center;
      color: #6b7280;
    }

    .detail-item {
      display: flex;
      gap: 4px;
    }

    .detail-item strong {
      color: #1f2937;
    }

    .detail-item.countdown {
      font-weight: 600;
      color: #3b82f6;
    }

    .detail-item.countdown strong {
      color: #1e40af;
    }

    .detail-item.error {
      color: #dc2626;
    }

    .detail-item.error strong {
      color: #991b1b;
    }

    /* Toast Notifications */
    .toast-container {
      position: fixed;
      top: 20px;
      right: 20px;
      z-index: 10000;
      display: flex;
      flex-direction: column;
      gap: 8px;
      pointer-events: none;
    }

    .toast {
      display: flex;
      align-items: center;
      gap: 10px;
      padding: 12px 16px;
      background: white;
      border-radius: 6px;
      box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
      font-size: 13px;
      font-weight: 500;
      pointer-events: auto;
      min-width: 300px;
    }

    .toast.toast-cases {
      border-left: 3px solid #3b82f6;
      background: rgba(59, 130, 246, 0.95);
      color: white;
    }

    .toast.toast-kpis {
      border-left: 3px solid #f59e0b;
      background: rgba(245, 158, 11, 0.95);
      color: white;
    }

    .toast-icon {
      flex-shrink: 0;
      font-size: 16px;
    }

    .toast-text {
      flex: 1;
    }

    /* Animations */
    @keyframes slideIn {
      from {
        transform: translateX(400px);
        opacity: 0;
      }
      to {
        transform: translateX(0);
        opacity: 1;
      }
    }

    @keyframes toastSlide {
      from {
        transform: translateX(400px) scale(0.9);
        opacity: 0;
      }
      to {
        transform: translateX(0) scale(1);
        opacity: 1;
      }
    }

    /* Responsive */
    @media (max-width: 768px) {
      .notification-container {
        left: 10px;
        right: 10px;
        top: 10px;
      }

      .alerts-wrapper {
        max-width: 100%;
      }

      .alert-card {
        padding: 12px;
      }

      .polling-status-bar {
        max-width: 100%;
      }

      .toast {
        min-width: 280px;
      }
    }
  `],
  animations: [
    trigger('slideIn', [
      transition(':enter', [
        style({ transform: 'translateX(400px)', opacity: 0 }),
        animate('300ms ease-out', style({ transform: 'translateX(0)', opacity: 1 })),
      ]),
      transition(':leave', [
        animate('300ms ease-in', style({ transform: 'translateX(400px)', opacity: 0 })),
      ]),
    ]),
    trigger('toastSlide', [
      transition(':enter', [
        style({ transform: 'translateX(400px) scale(0.9)', opacity: 0 }),
        animate('300ms ease-out', style({ transform: 'translateX(0) scale(1)', opacity: 1 })),
      ]),
      transition(':leave', [
        animate('300ms ease-in', style({ transform: 'translateX(400px) scale(0.9)', opacity: 0 })),
      ]),
    ]),
  ],
})
export class DataNotificationPanelComponent implements OnInit, OnDestroy {
  activeAlerts: NotificationAlert[] = [];
  toastNotifications: NotificationAlert[] = [];
  pollingState$ = this.pollingService.getPollingState();

  private destroy$ = new Subject<void>();
  private alertTimeouts = new Map<string, NodeJS.Timeout>();
  private toastTimeout: NodeJS.Timeout | null = null;

  constructor(private pollingService: AranPollingService) {}

  ngOnInit(): void {
    // Subscribe to notifications with debounce to prevent spam
    this.pollingService
      .getNotifications()
      .pipe(debounceTime(300), takeUntil(this.destroy$))
      .subscribe((notification) => {
        this.addNotification(notification);
      });
  }

  private addNotification(notification: DataChangeNotification): void {
    const alertId = `${notification.affectedAccidentNo}-${notification.type}-${Date.now()}`;

    // Determine icon and color
    const { icon, color } = this.getAlertStyle(notification);

    const alert: NotificationAlert = {
      id: alertId,
      notification,
      isShowing: true,
      timeRemaining: 5000,
      icon,
      color,
    };

    // Add to alerts
    this.activeAlerts.push(alert);

    // Add to toasts (separate list for top-right corner)
    this.toastNotifications.push(alert);

    // Auto-dismiss alert after 5 seconds
    const timeout = setTimeout(() => {
      this.dismissAlert(alertId);
    }, 5000);

    this.alertTimeouts.set(alertId, timeout);

    // Auto-dismiss toast after 3 seconds
    if (this.toastTimeout) {
      clearTimeout(this.toastTimeout);
    }
    this.toastTimeout = setTimeout(() => {
      this.toastNotifications = [];
    }, 3000);

    // Play notification sound if available
    this.playNotificationSound();
  }

  private getAlertStyle(notification: DataChangeNotification): { icon: string; color: string } {
    if (notification.type === 'cases') {
      return {
        icon: '🚨',
        color: 'info',
      };
    } else {
      return {
        icon: '📊',
        color: 'warning',
      };
    }
  }

  dismissAlert(alertId: string): void {
    const index = this.activeAlerts.findIndex((a) => a.id === alertId);
    if (index > -1) {
      this.activeAlerts.splice(index, 1);
    }

    // Clear timeout
    const timeout = this.alertTimeouts.get(alertId);
    if (timeout) {
      clearTimeout(timeout);
      this.alertTimeouts.delete(alertId);
    }
  }

  getTimeSince(date: Date): string {
    const seconds = Math.floor((Date.now() - date.getTime()) / 1000);
    if (seconds < 60) return `${seconds}s ago`;
    const minutes = Math.floor(seconds / 60);
    if (minutes < 60) return `${minutes}m ago`;
    const hours = Math.floor(minutes / 60);
    return `${hours}h ago`;
  }

  private playNotificationSound(): void {
    // Use Web Audio API or simple alert if available
    try {
      const audioContext = new (window as any).AudioContext || new (window as any).webkitAudioContext();
      const oscillator = audioContext.createOscillator();
      const gain = audioContext.createGain();

      oscillator.connect(gain);
      gain.connect(audioContext.destination);

      oscillator.frequency.value = 800;
      oscillator.type = 'sine';

      gain.gain.setValueAtTime(0.3, audioContext.currentTime);
      gain.gain.exponentialRampToValueAtTime(0.01, audioContext.currentTime + 0.5);

      oscillator.start(audioContext.currentTime);
      oscillator.stop(audioContext.currentTime + 0.5);
    } catch (error) {
      // Browser doesn't support or user blocked audio
      console.log('Audio notification unavailable');
    }
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();

    // Clear all timeouts
    this.alertTimeouts.forEach((timeout) => clearTimeout(timeout));
    if (this.toastTimeout) {
      clearTimeout(this.toastTimeout);
    }
  }
}
