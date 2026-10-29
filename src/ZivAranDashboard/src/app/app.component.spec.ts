import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { AppComponent } from './app.component';
import { httpLoggingInterceptor } from './core/interceptors/http-logging.interceptor';
import { POLL_INTERVAL_MS } from './core/services/aran-polling.service';
import { environment } from '../environments/environment';

describe('Dashboard polling startup', () => {
  let fixture: ComponentFixture<AppComponent>;
  let http: HttpTestingController;
  let log: jasmine.Spy;
  const pollUrl = `${environment.apiUrl}/aran/poll`;
  const response = { casualties: [], kpi: null, serverTime: '2026-01-01T00:00:00Z' };

  beforeEach(() => {
    log = spyOn(console, 'log');
    TestBed.configureTestingModule({
      imports: [AppComponent, NoopAnimationsModule],
      providers: [
        provideHttpClient(withInterceptors([httpLoggingInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  function createDashboard(): HTMLInputElement {
    fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();
    tick(0);
    fixture.detectChanges();
    return fixture.nativeElement.querySelector('app-ziv-header input');
  }

  afterEach(() => {
    fixture.destroy();
    http.verify();
  });

  it('keeps an unsubmitted incident number across clock updates without polling', fakeAsync(() => {
    const input = createDashboard();
    input.value = 'INC001';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    tick(2000);
    fixture.detectChanges();

    expect(input.value).toBe('INC001');
    expect(fixture.componentInstance.selectedAccidentNo()).toBe('');
    http.expectNone(pollUrl);
    fixture.destroy();
  }));

  for (const submitEvent of ['change', 'keydown']) {
    it(`starts and logs polling on ${submitEvent}, then polls every minute`, fakeAsync(() => {
      const input = createDashboard();
      input.value = 'INC001';
      input.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      tick(1000);
      fixture.detectChanges();

      input.dispatchEvent(submitEvent === 'keydown'
        ? new KeyboardEvent('keydown', { key: 'Enter' })
        : new Event('change'));
      fixture.detectChanges();
      tick(0);

      const request = http.expectOne(pollUrl);
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({ accidentNo: 'INC001', search: null });
      expect(log).toHaveBeenCalledWith(
        `[HTTP →] POST ${pollUrl}`, { accidentNo: 'INC001', search: null }
      );
      request.flush(response);
      expect(log).toHaveBeenCalledWith('[AranPolling] response for', 'INC001', response);
      expect(log).toHaveBeenCalledWith(
        jasmine.stringMatching(/^\[HTTP ←\] POST /), response
      );

      // Blurring after Enter must not restart polling.
      input.dispatchEvent(new Event('change'));
      fixture.detectChanges();
      tick(0);
      http.expectNone(pollUrl);

      tick(POLL_INTERVAL_MS);
      http.expectOne(pollUrl).flush(response);
      fixture.destroy();
    }));
  }
});
