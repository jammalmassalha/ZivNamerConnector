// Casualty / Patient models for ARAN dashboard

export interface IncidentOption {
  accidentNo: string;
  accidentPlace?: string | null;
  accidentDate?: string | null;
  accidentTime?: string | null;
}

export interface PatientMovement {
  sequenceNumber: number;
  movementCode?: string | null;
  movementType?: string | null;
  timestamp?: string | null;
  medicalDept?: string | null;
  nursingUnit?: string | null;
}

export interface AranPatientCase {
  caseId: string;
  patientId?: string | null;
  nationalId?: string | null;
  firstName?: string | null;
  lastName?: string | null;
  fullName?: string | null;
  dateOfBirth?: string | null;
  accidentNo: string;
  mainInjury?: string | null;
  triageCode?: 'RED' | 'YELLOW' | 'GREEN' | 'BLACK' | null;
  triageLabel?: string | null;
  currentMovementCategory?: string | null;
  currentStatus?: string | null;
  location?: string | null;
  department?: string | null;
  lastMovementTime?: string | null;
  movements: PatientMovement[];
}

export interface AranDashboardPayload {
  incidents: IncidentOption[];
  selectedIncident: IncidentOption | null;
  cases: AranPatientCase[];
}

export interface AranPatientUpdate {
  accidentNo: string;
  caseId: string;
  movementCategory?: string | null;
  currentStatus?: string | null;
  location?: string | null;
  updatedAt?: string;
}

export interface Casualty {
  caseNumber: string;
  patientID?: string;
  accidentNo?: string;
  caseType?: string;
  currentMovement?: string;
  currentLocation?: string;
  triageStatus?: 'RED' | 'YELLOW' | 'GREEN' | 'BLACK';
  patientType?: string;
  lastMovementTime?: Date;
  updatedAt: Date;
}

export interface KpiSummary {
  total: number;
  red: number;
  yellow: number;
  green: number;
  black: number;
  inTreatment: number;
  discharged: number;
}

export interface MovementHistoryItem {
  sequenceNumber: number;
  movementType?: string;
  movementCode?: string;
  timestamp?: Date;
  room?: string;
  bed?: string;
  medicalDept?: string;
  nursingUnit?: string;
}

export interface PatientHistory {
  caseNumber: string;
  movements: MovementHistoryItem[];
}

export interface SignalRCasualtyUpdate {
  accidentNo: string;
  casualties: Casualty[];
  updatedAt: Date;
}

export interface SignalRKpiUpdate {
  accidentNo: string;
  kpi: KpiSummary;
  updatedAt: Date;
}

export interface SignalRSystemEvent {
  eventType: string;
  message?: string;
  connectionId?: string;
  timestamp: Date;
  exception?: string;
}
