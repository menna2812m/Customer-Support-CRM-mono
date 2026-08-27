import { TestBed } from '@angular/core/testing';
import { HttpTestingController } from '@angular/common/http/testing';
import { provideCrmTesting } from '@crm/core/testing';
import { DiagnosticsPage } from './diagnostics.page';

/**
 * Spec FR-032 and FR-034: the reference screen handles every mandated state, and a server-side
 * validation failure lands on the field it belongs to rather than disappearing.
 */
describe('DiagnosticsPage', () => {
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DiagnosticsPage],
      providers: [provideCrmTesting()],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function renderWithItems(count: number) {
    const fixture = TestBed.createComponent(DiagnosticsPage);
    fixture.detectChanges();

    http
      .expectOne((request) => request.url === '/api/v1/diagnostics/items')
      .flush({
        items: Array.from({ length: count }, (_, index) => ({
          id: `id-${index}`,
          name: `Diagnostic item ${index}`,
          createdAt: '2026-08-26T10:00:00Z',
        })),
        page: 1,
        pageSize: 10,
        totalCount: count,
        totalPages: Math.ceil(count / 10),
      });

    await fixture.whenStable();
    fixture.detectChanges();

    return fixture;
  }

  it('renders the returned page of items', async () => {
    const fixture = await renderWithItems(3);

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Diagnostic item 0');
    expect(text).toContain('Page 1 of 1');
  });

  it('shows the empty state rather than an error when the page has no rows', async () => {
    const fixture = await renderWithItems(0);

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Nothing here yet');
  });

  it('shows the forbidden state when the caller lacks the permission', async () => {
    const fixture = TestBed.createComponent(DiagnosticsPage);
    fixture.detectChanges();

    http
      .expectOne((request) => request.url === '/api/v1/diagnostics/items')
      .flush(
        { code: 'forbidden', correlationId: 'corr-9' },
        { status: 403, statusText: 'Forbidden' },
      );

    await fixture.whenStable();
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('do not have access');
  });

  it('shows the server error state with the correlation id, and retries on demand', async () => {
    const fixture = TestBed.createComponent(DiagnosticsPage);
    fixture.detectChanges();

    http
      .expectOne((request) => request.url === '/api/v1/diagnostics/items')
      .flush(
        { code: 'unexpected_error', correlationId: 'corr-42' },
        { status: 500, statusText: 'Server Error' },
      );

    await fixture.whenStable();
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.textContent).toContain('corr-42');

    element.querySelector<HTMLButtonElement>('button')?.click();
    await fixture.whenStable();

    // Retrying must re-issue the request rather than leaving the user stuck on the error state.
    http
      .expectOne((request) => request.url === '/api/v1/diagnostics/items')
      .flush({
        items: [],
        page: 1,
        pageSize: 10,
        totalCount: 0,
        totalPages: 0,
      });
  });

  it('binds server-side field failures onto the matching form controls', async () => {
    const fixture = await renderWithItems(1);
    const component = fixture.componentInstance as unknown as {
      form: {
        patchValue: (value: unknown) => void;
        get: (field: string) => { errors: unknown } | null;
      };
      submit: () => void;
    };

    component.form.patchValue({ message: 'ok', repeatCount: 5 });
    component.submit();

    http.expectOne('/api/v1/diagnostics/echo').flush(
      {
        code: 'validation_failed',
        correlationId: 'corr-7',
        errors: [{ field: 'repeatCount', code: 'range', message: 'Out of range.' }],
      },
      { status: 400, statusText: 'Bad Request' },
    );

    await fixture.whenStable();
    fixture.detectChanges();

    // The server is the authority on validation: its rejection must reach the field, not vanish.
    expect(component.form.get('repeatCount')?.errors).toMatchObject({ server: ['range'] });
  });
});
