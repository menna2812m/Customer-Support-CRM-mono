import { TestBed } from '@angular/core/testing';
import { HttpTestingController } from '@angular/common/http/testing';
import { AppError } from '@crm/core';
import { provideCrmTesting } from '@crm/core/testing';
import { DiagnosticsApiService } from './diagnostics-api.service';

/**
 * Spec FR-029 and FR-031: the data-access service is the only HTTP caller, and every failure it
 * surfaces has already been normalized into an AppError - features never see an HttpErrorResponse.
 */
describe('DiagnosticsApiService', () => {
  let service: DiagnosticsApiService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideCrmTesting()] });

    service = TestBed.inject(DiagnosticsApiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('requests a page using the shared pagination parameters', () => {
    service.listItems(3, 10, '-createdAt').subscribe();

    const request = http.expectOne((candidate) => candidate.url === '/api/v1/diagnostics/items');

    expect(request.request.params.get('page')).toBe('3');
    expect(request.request.params.get('pageSize')).toBe('10');
    expect(request.request.params.get('sort')).toBe('-createdAt');
    request.flush({ items: [], page: 3, pageSize: 10, totalCount: 0, totalPages: 0 });
  });

  it('omits the sort parameter when none is requested', () => {
    service.listItems(1, 25).subscribe();

    const request = http.expectOne((candidate) => candidate.url === '/api/v1/diagnostics/items');

    expect(request.request.params.has('sort')).toBe(false);
    request.flush({ items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0 });
  });

  it('surfaces a validation failure as an AppError with field errors attached', async () => {
    const failure = new Promise<AppError>((resolve) => {
      service.echo({ message: '', repeatCount: 99 }).subscribe({
        error: (error: AppError) => resolve(error),
      });
    });

    http.expectOne('/api/v1/diagnostics/echo').flush(
      {
        code: 'validation_failed',
        correlationId: 'corr-1',
        errors: [
          { field: 'message', code: 'required', message: 'Message is required.' },
          { field: 'repeatCount', code: 'range', message: 'Out of range.' },
        ],
      },
      { status: 400, statusText: 'Bad Request' },
    );

    const error = await failure;

    expect(error.kind).toBe('validation');
    expect(error.correlationId).toBe('corr-1');
    expect(error.fieldErrors?.['repeatCount'][0].code).toBe('range');
  });

  it('surfaces a forbidden response as the forbidden kind', async () => {
    const failure = new Promise<AppError>((resolve) => {
      service.listItems(1, 25).subscribe({ error: (error: AppError) => resolve(error) });
    });

    http
      .expectOne((candidate) => candidate.url === '/api/v1/diagnostics/items')
      .flush(
        { code: 'forbidden', correlationId: 'corr-2' },
        { status: 403, statusText: 'Forbidden' },
      );

    expect((await failure).kind).toBe('forbidden');
  });
});
