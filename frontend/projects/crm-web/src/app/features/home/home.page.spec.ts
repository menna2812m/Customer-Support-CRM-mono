import { TestBed } from '@angular/core/testing';
import { HttpTestingController } from '@angular/common/http/testing';
import { provideCrmTesting } from '@crm/core/testing';
import { HomePage } from './home.page';

/**
 * Spec US1 acceptance: the home screen reaches the API and renders a mandated state for both
 * outcomes - never a blank page (Constitution X).
 */
describe('HomePage', () => {
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [HomePage],
      providers: [provideCrmTesting()],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('renders the healthy report returned by the API', async () => {
    const fixture = TestBed.createComponent(HomePage);
    fixture.detectChanges();

    http.expectOne('/health/ready').flush({
      status: 'Healthy',
      checks: [{ name: 'database', status: 'Healthy', durationMs: 12 }],
    });

    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Healthy');
    expect(text).toContain('database');
  });

  it('renders the server error state, with the correlation id, when the API fails', async () => {
    const fixture = TestBed.createComponent(HomePage);
    fixture.detectChanges();

    http.expectOne('/health/ready').flush(
      {
        code: 'unexpected_error',
        correlationId: 'abc123',
        title: 'The request could not be completed.',
      },
      { status: 500, statusText: 'Server Error' },
    );

    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Something went wrong');
    // The correlation identifier is the only handle support can use - it must reach the user.
    expect(text).toContain('abc123');
  });
});
