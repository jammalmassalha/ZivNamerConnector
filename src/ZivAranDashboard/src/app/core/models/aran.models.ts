// Casualty / Patient models for ARAN dashboard

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
