import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { provideCrmTesting } from '@crm/core/testing';
import { of } from 'rxjs';
import { DepartmentsPage } from './departments.page';

/**
 * Stands in for the move dialog so this spec can assert what the page does with the outcome. The
 * dialog's own behaviour - closing with the result, staying open on a refusal - is covered in
 * move-team.dialog.spec.ts.
 */
const MOVE_RESULT = { team: { id: 't1', departmentId: 'd2' }, membersReassigned: 3 };

/**
 * The departments screen. What is worth testing here is not that a list renders, but that the
 * containment rule and the refusals survive the trip to the interface.
 */
describe('DepartmentsPage', () => {
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DepartmentsPage],
      providers: [provideCrmTesting()],
    });

    // The page imports MatDialogModule, so MatDialog is a component-level provider; overriding the
    // token reaches it where a root-level provider would be shadowed. Has to happen before the
    // module is instantiated.
    TestBed.overrideProvider(MatDialog, {
      useValue: { open: () => ({ afterClosed: () => of(MOVE_RESULT) }) },
    });

    await TestBed.compileComponents();

    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function department(id: string, nameEn: string, isActive = true) {
    return { id, nameAr: `${nameEn} بالعربية`, nameEn, code: id.toUpperCase(), isActive };
  }

  function render(items: ReturnType<typeof department>[]) {
    const fixture = TestBed.createComponent(DepartmentsPage);
    fixture.detectChanges();

    http
      .expectOne((request) => request.url === '/api/v1/organization/departments')
      .flush({
        items,
        page: 1,
        pageSize: 100,
        totalCount: items.length,
        totalPages: 1,
      });

    fixture.detectChanges();

    return fixture;
  }

  it('shows the departments it loaded', () => {
    const fixture = render([department('d1', 'Technical Support')]);

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Technical Support');
  });

  it('reports the empty state rather than an empty card', () => {
    const fixture = render([]);

    // The six mandated states are handled by crm-state-container; an empty list is a success with
    // nothing in it, never an error.
    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('Technical Support');
  });

  it('surfaces a refused delete instead of silently doing nothing', () => {
    const fixture = render([department('d1', 'Operations')]);
    const page = fixture.componentInstance as unknown as {
      remove: (unit: { id: string }) => void;
      formError: () => { code?: string } | null;
    };

    page.remove({ id: 'd1' });

    http
      .expectOne('/api/v1/organization/departments/d1')
      .flush(
        { code: 'organization_has_dependents', detail: 'It still has 2 team(s).' },
        { status: 409, statusText: 'Conflict' },
      );

    fixture.detectChanges();

    // A delete refused because teams depend on it must reach the administrator - that refusal is
    // the only thing telling them what to fix first.
    expect(page.formError()?.code).toBe('organization_has_dependents');
  });

  // The count is the only evidence the move did more than change one row, and it is the one thing
  // an administrator cannot check for themselves (spec FR-015).
  it('reports how many people a completed move reassigned', () => {
    const fixture = render([department('d1', 'Operations')]);
    const page = fixture.componentInstance as unknown as {
      move: (team: { id: string; departmentId: string }) => void;
      moveOutcome: () => { membersReassigned: number } | null;
    };

    page.move({ id: 't1', departmentId: 'd1' });
    fixture.detectChanges();

    expect(page.moveOutcome()?.membersReassigned).toBe(3);

    // A completed move refreshes both lists; flushing them keeps the verify in afterEach honest.
    http
      .expectOne((request) => request.url === '/api/v1/organization/departments/d1/teams')
      .flush({
        items: [],
        page: 1,
        pageSize: 100,
        totalCount: 0,
        totalPages: 1,
      });
    http
      .expectOne((request) => request.url === '/api/v1/organization/departments')
      .flush({
        items: [],
        page: 1,
        pageSize: 100,
        totalCount: 0,
        totalPages: 1,
      });
  });

  it('loads a department’s teams from under that department', () => {
    const fixture = render([department('d1', 'Billing')]);
    const page = fixture.componentInstance as unknown as {
      openTeams: (unit: { id: string }) => void;
    };

    page.openTeams({ id: 'd1' });

    // The containment rule reaches the URL: teams are never fetched from a top-level route.
    const request = http.expectOne('/api/v1/organization/departments/d1/teams?page=1&pageSize=100');

    expect(request.request.method).toBe('GET');
    request.flush({ items: [], page: 1, pageSize: 100, totalCount: 0, totalPages: 0 });
  });
});
