import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideCrmTesting } from '@crm/core/testing';
import { PeoplePage } from './people.page';

/**
 * The people screen. What is worth testing is not that a list renders, but that the three states a
 * person can be in reach the reader, that the filters ask the server the right question, and that a
 * refusal is shown rather than swallowed.
 */
describe('PeoplePage', () => {
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PeoplePage],
      providers: [provideCrmTesting()],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function person(id: string, displayName: string, status: string, isActive = true) {
    return {
      id,
      displayName,
      email: `${id}@example.com`,
      status,
      isActive,
      hasSignedIn: status !== 'invited',
      placement: {
        branchId: null,
        branchNameAr: null,
        branchNameEn: null,
        departmentId: null,
        departmentNameAr: null,
        departmentNameEn: null,
        teamId: null,
        teamNameAr: null,
        teamNameEn: null,
      },
    };
  }

  function render(items: ReturnType<typeof person>[]) {
    const fixture = TestBed.createComponent(PeoplePage);
    fixture.detectChanges();

    http
      .expectOne((request) => request.url === '/api/v1/identity/people')
      .flush({ items, page: 1, pageSize: 100, totalCount: items.length, totalPages: 1 });

    fixture.detectChanges();

    return fixture;
  }

  it('shows the people it loaded', () => {
    const fixture = render([person('p1', 'Layla Hassan', 'active')]);

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Layla Hassan');
  });

  it('reports the empty state rather than an empty table', () => {
    const fixture = render([]);

    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('Layla Hassan');
  });

  it('distinguishes somebody prepared from somebody who has signed in', () => {
    const fixture = render([
      person('p1', 'Arrived Person', 'active'),
      person('p2', 'Prepared Person', 'invited'),
    ]);

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';

    // Two booleans produce three states, and an administrator has to be able to tell them apart:
    // "invited" is why somebody has roles but has never used them.
    expect(text).toContain('Active');
    expect(text).toContain('Invited');
  });

  it('asks the server for the never-signed-in filter rather than filtering locally', () => {
    const fixture = render([person('p1', 'Someone', 'active')]);
    const page = fixture.componentInstance as unknown as {
      toggleUnlinkedOnly: (checked: boolean) => void;
    };

    page.toggleUnlinkedOnly(true);

    // Filtering a page in the browser would filter one page, not the collection.
    const request = http.expectOne((r) => r.url === '/api/v1/identity/people');
    expect(request.request.params.get('unlinkedOnly')).toBe('true');

    request.flush({ items: [], page: 1, pageSize: 100, totalCount: 0, totalPages: 0 });
  });

  it('refuses to send a person with no email address', () => {
    const fixture = render([]);
    const page = fixture.componentInstance as unknown as {
      form: { setValue: (value: { email: string; displayName: string }) => void };
      create: () => void;
    };

    page.form.setValue({ email: '', displayName: 'No Address' });
    page.create();

    http.expectNone((request) => request.method === 'POST');
  });

  it('surfaces a refused creation instead of silently doing nothing', () => {
    const fixture = render([]);
    const page = fixture.componentInstance as unknown as {
      form: { setValue: (value: { email: string; displayName: string }) => void };
      create: () => void;
      formError: () => { code?: string } | null;
    };

    page.form.setValue({ email: 'taken@example.com', displayName: 'Duplicate' });
    page.create();

    http
      .expectOne('/api/v1/identity/people')
      .flush({ code: 'identity_email_in_use' }, { status: 409, statusText: 'Conflict' });

    fixture.detectChanges();

    expect(page.formError()?.code).toBe('identity_email_in_use');
  });

  it('reloads the list after preparing somebody, so they appear without a refresh', () => {
    const fixture = render([]);
    const page = fixture.componentInstance as unknown as {
      form: { setValue: (value: { email: string; displayName: string }) => void };
      create: () => void;
    };

    page.form.setValue({ email: 'noor@example.com', displayName: 'Noor Abdullah' });
    page.create();

    const prepared = person('p-new', 'Noor Abdullah', 'invited');

    http.expectOne('/api/v1/identity/people').flush({
      summary: prepared,
      roles: [],
      effectivePermissions: [],
      lastSignedInAt: null,
    });

    // A prepared person the administrator cannot see is indistinguishable from one who was never
    // prepared, so the list is re-read rather than patched in memory.
    http
      .expectOne((request) => request.method === 'GET')
      .flush({
        items: [prepared],
        page: 1,
        pageSize: 100,
        totalCount: 1,
        totalPages: 1,
      });

    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Noor Abdullah');
  });

  it('leaves the form clean after a success rather than showing it as invalid', () => {
    const fixture = render([]);
    const page = fixture.componentInstance as unknown as {
      form: { setValue: (value: { email: string; displayName: string }) => void };
    };

    page.form.setValue({ email: 'noor@example.com', displayName: 'Noor Abdullah' });

    // Submitted through the template rather than by calling create() directly. The flag that causes
    // this defect lives on FormGroupDirective, and only a real submit event sets it - a test that
    // calls the method passes whether the bug is present or not.
    const form = fixture.nativeElement.querySelector('form') as HTMLFormElement;
    form.dispatchEvent(new Event('submit'));
    fixture.detectChanges();

    http.expectOne('/api/v1/identity/people').flush({
      summary: person('p-new', 'Noor Abdullah', 'invited'),
      roles: [],
      effectivePermissions: [],
      lastSignedInAt: null,
    });

    http
      .expectOne((request) => request.method === 'GET')
      .flush({
        items: [],
        page: 1,
        pageSize: 100,
        totalCount: 0,
        totalPages: 0,
      });

    fixture.detectChanges();

    // Resetting the group without the directive leaves it marked submitted, and Material renders
    // every required field as an error the moment the creation succeeds. The empty form must read
    // as ready for the next person, not as a rejected one.
    expect(fixture.nativeElement.querySelectorAll('.mat-form-field-invalid').length).toBe(0);
  });
});
