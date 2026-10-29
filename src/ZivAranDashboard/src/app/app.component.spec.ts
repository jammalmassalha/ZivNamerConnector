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
});
