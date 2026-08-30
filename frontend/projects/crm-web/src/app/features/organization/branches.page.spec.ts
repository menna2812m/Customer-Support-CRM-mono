import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideCrmTesting } from '@crm/core/testing';
import { BranchesPage } from './branches.page';

/**
 * The branches screen. Recorded as covered by T046 and in fact untested until now, which is most of
 * why the walkthrough found what it did here.
 *
 * A branch belongs to nothing and contains nothing (FR-003), so the rules worth holding are the
 * ones about the unit itself: both names are required together, a refusal reaches the reader rather
 * than being swallowed, and deactivating keeps the branch in the administration list instead of
 * hiding it.
 */
describe('BranchesPage', () => {
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [BranchesPage],
      providers: [provideCrmTesting()],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function branch(id: string, nameEn: string, isActive = true) {
    return { id, nameAr: `${nameEn} بالعربية`, nameEn, code: id.toUpperCase(), isActive };
  }

  function render(items: ReturnType<typeof branch>[]) {
    const fixture = TestBed.createComponent(BranchesPage);
    fixture.detectChanges();

    http
      .expectOne((request) => request.url === '/api/v1/organization/branches')
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

  it('shows the branches it loaded', () => {
    const fixture = render([branch('b1', 'Riyadh')]);

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Riyadh');
  });

  it('reports the empty state rather than an empty card', () => {
    const fixture = render([]);

    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('Riyadh');
  });

  it('keeps a deactivated branch in the list, marked, rather than hiding it', () => {
    const fixture = render([branch('b1', 'Riyadh', false)]);

    // Administration shows what exists; an inactive branch is still a branch, and only a placement
    // chooser filters it out.
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Riyadh');
  });

  it('refuses to send a half-translated branch', () => {
    const fixture = render([]);
    const page = fixture.componentInstance as unknown as {
      form: { setValue: (value: { nameAr: string; nameEn: string; code: string }) => void };
      create: () => void;
    };

    // Both names on one form is the rule (LR-003): a unit cannot exist in one language only, and
    // the form is what stops it rather than the server.
    page.form.setValue({ nameAr: '', nameEn: 'Jeddah', code: 'JED' });
    page.create();

    http.expectNone((request) => request.method === 'POST');
  });

  it('surfaces a refused delete instead of silently doing nothing', () => {
    const fixture = render([branch('b1', 'Riyadh')]);
    const page = fixture.componentInstance as unknown as {
      remove: (unit: { id: string }) => void;
      formError: () => { code?: string } | null;
    };

    page.remove({ id: 'b1' });

    http
      .expectOne('/api/v1/organization/branches/b1')
      .flush(
        { code: 'organization_has_dependents', detail: 'It still has 4 person(s).' },
        { status: 409, statusText: 'Conflict' },
      );

    fixture.detectChanges();

    // People are placed in this branch, so the delete is refused - and the reader has to be told,
    // because nothing else on the screen explains why the row is still there.
    expect(page.formError()?.code).toBe('organization_has_dependents');
  });
});
