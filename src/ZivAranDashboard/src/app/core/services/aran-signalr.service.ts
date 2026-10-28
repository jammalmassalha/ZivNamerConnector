import { Injectable } from '@angular/core';
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { BehaviorSubject, Observable } from 'rxjs';
import {
  SignalRCasualtyUpdate,
  SignalRKpiUpdate,
  SignalRSystemEvent,
} from '../models/aran.models';
import { SIGNALR_CONFIG } from '../constants/sap-movement.constant';

@Injectable({
  providedIn: 'root',
})
export class AranSignalRService {
  private hubConnection: HubConnection | null = null;
  private isConnected$ = new BehaviorSubject<boolean>(false);
  private casualtyUpdates$ = new BehaviorSubject<SignalRCasualtyUpdate | null>(null);
  private kpiUpdates$ = new BehaviorSubject<SignalRKpiUpdate | null>(null);
  private systemEvents$ = new BehaviorSubject<SignalRSystemEvent | null>(null);

  constructor() {
    this.initializeHub();
  }

  private initializeHub(): void {
    this.hubConnection = new HubConnectionBuilder()
      .withUrl(SIGNALR_CONFIG.HUB_URL, {
        skipNegotiation: true,
        transport: 1, // WebSocket
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000])
      .configureLogging(LogLevel.Information)
      .build();

    // Register event listeners
    this.hubConnection.on(SIGNALR_CONFIG.LISTENERS.RECEIVE_UPDATES, (update: SignalRCasualtyUpdate) => {
      this.casualtyUpdates$.next(update);
    });

    this.hubConnection.on(SIGNALR_CONFIG.LISTENERS.RECEIVE_KPI, (update: SignalRKpiUpdate) => {
      this.kpiUpdates$.next(update);
    });

    this.hubConnection.on(SIGNALR_CONFIG.LISTENERS.RECEIVE_EVENT, (event: SignalRSystemEvent) => {
      this.systemEvents$.next(event);
    });

    // Connection state handlers
    this.hubConnection.onreconnecting(() => {
      this.isConnected$.next(false);
    });

    this.hubConnection.onreconnected(() => {
      this.isConnected$.next(true);
    });
  }

  /**
   * Connect to SignalR hub
   */
  connect(): Promise<void> {
    if (!this.hubConnection) {
      return Promise.reject('Hub not initialized');
    }

    if (this.hubConnection.state === 'Connected') {
      this.isConnected$.next(true);
      return Promise.resolve();
    }

    return this.hubConnection
      .start()
      .then(() => {
        this.isConnected$.next(true);
      })
      .catch((err) => {
        console.error('SignalR connection failed:', err);
        throw err;
      });
  }

  /**
   * Disconnect from SignalR hub
   */
  disconnect(): Promise<void> {
    if (!this.hubConnection) {
      return Promise.resolve();
    }

    return this.hubConnection.stop().then(() => {
      this.isConnected$.next(false);
    });
  }

  /**
   * Join an incident group to receive updates
   */
  joinIncidentGroup(accidentNo: string): Promise<void> {
    if (!this.hubConnection || !this.isConnected$.value) {
      return Promise.reject('Hub not connected');
    }

    return this.hubConnection.invoke(SIGNALR_CONFIG.METHODS.JOIN_INCIDENT, accidentNo);
  }

  /**
   * Leave an incident group
   */
  leaveIncidentGroup(accidentNo: string): Promise<void> {
    if (!this.hubConnection || !this.isConnected$.value) {
      return Promise.reject('Hub not connected');
    }

    return this.hubConnection.invoke(SIGNALR_CONFIG.METHODS.LEAVE_INCIDENT, accidentNo);
  }

  /**
   * Observable stream of casualty updates
   */
  getCasualtyUpdates(): Observable<SignalRCasualtyUpdate | null> {
    return this.casualtyUpdates$.asObservable();
  }

  /**
   * Observable stream of KPI updates
   */
  getKpiUpdates(): Observable<SignalRKpiUpdate | null> {
    return this.kpiUpdates$.asObservable();
  }

  /**
   * Observable stream of system events
   */
  getSystemEvents(): Observable<SignalRSystemEvent | null> {
    return this.systemEvents$.asObservable();
  }

  /**
   * Observable connection status
   */
  isConnected(): Observable<boolean> {
    return this.isConnected$.asObservable();
  }

  /**
   * Get current connection status
   */
  getConnectionStatus(): boolean {
    return this.isConnected$.value;
  }
}
