import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { of, Subject } from 'rxjs';
import { AppComponent } from './app.component';
import { AranService } from './core/services/aran.service';
import { AranPatientUpdate } from './core/models/aran.models';

describe('ARAN dashboard startup', () => {
  let fixture: ComponentFixture<AppComponent>;
  let aranService: jasmine.SpyObj<AranService>;
  const patientUpdates = new Subject<AranPatientUpdate>();
  const connectionStatus = new Subject<boolean>();
  const latestPayload = {
    incidents: [
      { accidentNo: 'ARAN-2', accidentPlace: 'צפת', accidentDate: '2026-10-29' },
      { accidentNo: 'ARAN-1', accidentPlace: 'חצור', accidentDate: '2026-10-28' },
    ],
    selectedIncident: { accidentNo: 'ARAN-2', accidentPlace: 'צפת', accidentDate: '2026-10-29' },
    cases: [],
  };

  beforeEach(() => {
    aranService = jasmine.createSpyObj<AranService>(
      'AranService',
      ['getLatestIncidents', 'getIncidentCases', 'connect', 'selectIncident', 'disconnect'],
      {
        patientUpdates$: patientUpdates.asObservable(),
        connectionStatus$: connectionStatus.asObservable(),
      }
    );
    aranService.getLatestIncidents.and.returnValue(of(latestPayload));
    aranService.getIncidentCases.and.returnValue(of([]));
    aranService.connect.and.resolveTo();
    aranService.selectIncident.and.resolveTo();
    aranService.disconnect.and.resolveTo();

    TestBed.configureTestingModule({
      imports: [AppComponent, NoopAnimationsModule],
      providers: [{ provide: AranService, useValue: aranService }],
    });
    fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();
  });

  afterEach(() => fixture.destroy());

  it('automatically selects the latest incident returned by the API', () => {
    const dashboard = fixture.nativeElement.querySelector('app-aran-dashboard');

    expect(aranService.getLatestIncidents).toHaveBeenCalled();
    expect(dashboard.textContent).toContain('ARAN-2');
    expect(aranService.selectIncident).toHaveBeenCalledWith('ARAN-2');
  });

  it('loads cases and joins the group when another incident is selected', () => {
    const dashboard = fixture.debugElement.children[0].componentInstance;

    dashboard.onIncidentChange('ARAN-1');

    expect(aranService.selectIncident).toHaveBeenCalledWith('ARAN-1');
    expect(aranService.getIncidentCases).toHaveBeenCalledWith('ARAN-1');
  });

  const makeCase = (caseId: string, accidentNo: string, extra: Record<string, unknown> = {}) => ({
    caseId,
    accidentNo,
    fullName: `Patient ${caseId}`,
    movements: [],
    ...extra,
  });

  it('keeps the manual selection on background refresh when no new incident arrived', () => {
    const dashboard = fixture.debugElement.children[0].componentInstance;
    dashboard.onIncidentChange('ARAN-1');

    dashboard.refreshInBackground();

    expect(dashboard.selectedIncident()?.accidentNo).toBe('ARAN-1');
  });

  it('selects a newly arrived incident on refresh and alerts', () => {
    const dashboard = fixture.debugElement.children[0].componentInstance;
    dashboard.onIncidentChange('ARAN-1');
    const newIncident = { accidentNo: 'ARAN-3', accidentPlace: 'טבריה', accidentDate: '2026-11-01' };
    aranService.getLatestIncidents.and.returnValue(
      of({ incidents: [newIncident, ...latestPayload.incidents], selectedIncident: newIncident, cases: [] })
    );

    dashboard.refreshInBackground();

    expect(dashboard.selectedIncident()?.accidentNo).toBe('ARAN-3');
    expect(dashboard.incidents().length).toBe(3);
    expect(aranService.selectIncident).toHaveBeenCalledWith('ARAN-3');
    expect(dashboard.toasts()[0].kind).toBe('incident');
  });

  it('adds new patients of the selected incident to the table, highlights them and alerts', () => {
    const dashboard = fixture.debugElement.children[0].componentInstance;
    aranService.getIncidentCases.and.returnValue(of([makeCase('100', 'ARAN-2'), makeCase('101', 'ARAN-2')]));

    dashboard.refreshInBackground();
    fixture.detectChanges();

    expect(aranService.getIncidentCases).toHaveBeenCalledWith('ARAN-2');
    expect(dashboard.cases().length).toBe(2);
    expect(dashboard.newCaseIds().has('100')).toBeTrue();
    expect(dashboard.toasts()[0].kind).toBe('new');
    const text = fixture.nativeElement.textContent;
    expect(text).toContain('Patient 100');
    expect(text).toContain('חדש');
  });

  it('highlights and alerts on changed patients without clearing the table', () => {
    const dashboard = fixture.debugElement.children[0].componentInstance;
    dashboard.cases.set([makeCase('100', 'ARAN-2', { currentStatus: 'קבלה' })]);
    aranService.getIncidentCases.and.returnValue(of([makeCase('100', 'ARAN-2', { currentStatus: 'אשפוז' })]));

    dashboard.refreshInBackground();

    expect(dashboard.cases()[0].currentStatus).toBe('אשפוז');
    expect(dashboard.highlightedCaseIds().has('100')).toBeTrue();
    expect(dashboard.newCaseIds().has('100')).toBeFalse();
    expect(dashboard.toasts()[0].kind).toBe('update');
  });

  it('alerts with a switch action when another incident gets new patients', () => {
    const dashboard = fixture.debugElement.children[0].componentInstance;
    dashboard.incidents.set([
      { ...latestPayload.incidents[0], patientCount: 0 },
      { ...latestPayload.incidents[1], patientCount: 1 },
    ]);
    aranService.getLatestIncidents.and.returnValue(
      of({
        ...latestPayload,
        incidents: [
          { ...latestPayload.incidents[0], patientCount: 0 },
          { ...latestPayload.incidents[1], patientCount: 3 },
        ],
      })
    );

    dashboard.refreshInBackground();
    const toast = dashboard.toasts()[0];

    expect(toast.accidentNo).toBe('ARAN-1');
    dashboard.onToastAction(toast);
    expect(dashboard.selectedIncident()?.accidentNo).toBe('ARAN-1');
  });
});
