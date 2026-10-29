import { Injectable, OnDestroy, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { HubConnection, HubConnectionBuilder, HubConnectionState } from '@microsoft/signalr';
import { Observable, Subject } from 'rxjs';
import {
  AranDashboardPayload,
  AranPatientCase,
  AranPatientUpdate,
} from '../models/aran.models';
import { environment } from '../../../environments/environment';

@Injectable({ providedIn: 'root' })
export class AranService implements OnDestroy {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/aran`;
  private readonly updates = new Subject<AranPatientUpdate>();
  private readonly connectionStates = new Subject<boolean>();
  private connection?: HubConnection;
  private selectedIncidentNo = '';

  readonly patientUpdates$: Observable<AranPatientUpdate> = this.updates.asObservable();
  readonly connectionStatus$: Observable<boolean> = this.connectionStates.asObservable();

  getLatestIncidents(): Observable<AranDashboardPayload> {
    return this.http.get<AranDashboardPayload>(`${this.apiUrl}/accidents/latest`);
  }

  getIncidentCases(accidentNo: string): Observable<AranPatientCase[]> {
    return this.http.get<AranPatientCase[]>(
      `${this.apiUrl}/accidents/${encodeURIComponent(accidentNo)}/cases`
    );
  }

  async connect(): Promise<void> {
    if (this.connection) return;

    const hubUrl = `${environment.apiUrl.replace(/\/api\/?$/i, '')}/hubs/aran`;
    this.connection = new HubConnectionBuilder()
      .withUrl(hubUrl)
      .withAutomaticReconnect()
      .build();
    this.connection.on('ReceiveAranPatientUpdate', (update: AranPatientUpdate) => {
      this.updates.next(update);
    });
    this.connection.onreconnecting(() => this.connectionStates.next(false));
    this.connection.onreconnected(async () => {
      this.connectionStates.next(true);
      await this.joinSelectedIncident();
    });
    this.connection.onclose(() => this.connectionStates.next(false));

    await this.connection.start();
    this.connectionStates.next(true);
    await this.joinSelectedIncident();
  }

  async selectIncident(accidentNo: string): Promise<void> {
    const previous = this.selectedIncidentNo;
    this.selectedIncidentNo = accidentNo;
    if (!this.connection || this.connection.state !== HubConnectionState.Connected) return;

    if (previous) await this.connection.invoke('LeaveIncidentGroup', previous);
    await this.joinSelectedIncident();
  }

  async disconnect(): Promise<void> {
    const connection = this.connection;
    this.connection = undefined;
    this.selectedIncidentNo = '';
    if (connection) await connection.stop();
    this.connectionStates.next(false);
  }

  ngOnDestroy(): void {
    void this.disconnect();
  }

  private async joinSelectedIncident(): Promise<void> {
    if (
      this.connection?.state === HubConnectionState.Connected &&
      this.selectedIncidentNo
    ) {
      await this.connection.invoke('JoinIncidentGroup', this.selectedIncidentNo);
    }
  }
}
