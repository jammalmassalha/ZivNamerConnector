import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Casualty, KpiSummary, PatientHistory } from '../models/aran.models';
import { environment } from '../../../environments/environment';

interface SyncAranRequest {
  accidentNo?: string;
  fromDate?: Date;
}

interface SyncAranResult {
  success: boolean;
  updatedCasesCount: number;
  errorMessage?: string;
  syncedAt: Date;
}

interface UpdateTriageRequest {
  triageStatus: string;
  notes?: string;
}

@Injectable({
  providedIn: 'root',
})
export class AranHttpService {
  private readonly apiUrl = `${environment.apiUrl}/aran`;

  constructor(private http: HttpClient) {}

  /**
   * Get active casualties for an incident
   */
  getCases(accidentNo: string, search?: string): Observable<Casualty[]> {
    let url = `${this.apiUrl}/cases?accidentNo=${encodeURIComponent(accidentNo)}`;
    if (search) {
      url += `&search=${encodeURIComponent(search)}`;
    }
    return this.http.get<Casualty[]>(url);
  }

  /**
   * Get KPI summary for an incident
   */
  getKpis(accidentNo: string): Observable<KpiSummary> {
    return this.http.get<KpiSummary>(`${this.apiUrl}/kpis?accidentNo=${encodeURIComponent(accidentNo)}`);
  }

  /**
   * Trigger manual sync of ARAN cases
   */
  syncAran(request?: SyncAranRequest): Observable<SyncAranResult> {
    return this.http.post<SyncAranResult>(`${this.apiUrl}/sync`, request || {});
  }

  /**
   * Update triage status for a patient
   */
  updateTriageStatus(caseNumber: string, request: UpdateTriageRequest): Observable<Casualty> {
    return this.http.put<Casualty>(
      `${this.apiUrl}/patient/${encodeURIComponent(caseNumber)}/triage`,
      request
    );
  }

  /**
   * Get movement history for a patient
   */
  getPatientHistory(caseNumber: string): Observable<PatientHistory> {
    return this.http.get<PatientHistory>(
      `${this.apiUrl}/patient/${encodeURIComponent(caseNumber)}/history`
    );
  }
}
