import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { AuthSession, AuthUser } from '@crm/core';
import { provideCrmTesting } from '@crm/core/testing';
import { PersonPage } from './person.page';

const PERSON_ID = '11111111-1111-1111-1111-111111111111';
const DEPARTMENT_ID = '22222222-2222-2222-2222-222222222222';
const TEAM_ID = '33333333-3333-3333-3333-333333333333';

/**
 * One person's screen. The rules worth pinning are the two the server also enforces: a department
 * that follows its team, and controls an administrator may not use on their own account.
 */
describe('PersonPage', () => {
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PersonPage],
      providers: [
        provideCrmTesting(),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({ id: PERSON_ID }) } },
        },
      ],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function signInAs(id: string): void {
    const user: AuthUser = {
      id,
      displayName: 'Administrator',
      email: 'admin@example.com',
      population: 'Staff',
      permissions: ['identity.view', 'identity.manage'],
      scope: null,
    };

    TestBed.inject(AuthSession).set('issued-credential', user);
  }

  function detail(overrides: { teamId?: string | null; departmentId?: string | null } = {}) {
    return {
      summary: {
        id: PERSON_ID,
        displayName: 'Layla Hassan',
        email: 'layla@example.com',
        status: 'active',
        isActive: true,
        hasSignedIn: true,
        placement: {
          branchId: null,
          branchNameAr: null,
          branchNameEn: null,
          departmentId: overrides.departmentId ?? null,
          departmentNameAr: 'الدعم',
          departmentNameEn: 'Support',
          teamId: overrides.teamId ?? null,
          teamNameAr: 'المستوى الأول',
          teamNameEn: 'Tier 1',
        },
      },
      roles: [{ id: 'r1', name: 'Agent' }],
      effectivePermissions: ['customers.view', 'tickets.view'],
      lastSignedInAt: null,
    };
  }

  function render(person = detail()) {
    const fixture = TestBed.createComponent(PersonPage);
    fixture.detectChanges();

    http.expectOne(`/api/v1/identity/people/${PERSON_ID}`).flush(person);
    http.expectOne('/api/v1/identity/roles').flush([
      { id: 'r1', name: 'Agent', permissions: ['customers.view'] },
      { id: 'r2', name: 'Administrator', permissions: ['identity.manage'] },
    ]);
    http
      .expectOne((r) => r.url === '/api/v1/organization/branches')
      .flush({ items: [], page: 1, pageSize: 100, totalCount: 0, totalPages: 0 });
    http
      .expectOne((r) => r.url === '/api/v1/organization/departments')
      .flush({ items: [], page: 1, pageSize: 100, totalCount: 0, totalPages: 0 });

    // A person already on a team needs that team's siblings loaded for the chooser.
    if (person.summary.placement.teamId && person.summary.placement.departmentId) {
      http
        .expectOne((r) => r.url.endsWith('/teams'))
        .flush({ items: [], page: 1, pageSize: 100, totalCount: 0, totalPages: 0 });
    }

    fixture.detectChanges();

    return fixture;
  }

  it('shows the roles held and the permissions they grant', () => {
    signInAs('somebody-else');
    const fixture = render();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';

    expect(text).toContain('Agent');

    // The consequence of the roles, shown beside them and never as something editable.
    expect(text).toContain('customers.view');
  });

  it('locks the department while a team is selected', () => {
    signInAs('somebody-else');
    const fixture = render(detail({ teamId: TEAM_ID, departmentId: DEPARTMENT_ID }));
    const page = fixture.componentInstance as unknown as {
      placement: { controls: { departmentId: { disabled: boolean } } };
    };

    // Derived, not chosen. A disabled control is how the rule becomes visible; the server refuses
    // a disagreeing department regardless of what the form allows.
    expect(page.placement.controls.departmentId.disabled).toBe(true);
  });

  it('frees the department again when the team is cleared', () => {
    signInAs('somebody-else');
    const fixture = render(detail({ teamId: TEAM_ID, departmentId: DEPARTMENT_ID }));
    const page = fixture.componentInstance as unknown as {
      onTeamChanged: (teamId: string) => void;
      placement: { controls: { departmentId: { disabled: boolean } } };
    };

    page.onTeamChanged('');

    expect(page.placement.controls.departmentId.disabled).toBe(false);
  });

  it('never sends a department alongside a team', () => {
    signInAs('somebody-else');
    const fixture = render(detail({ teamId: TEAM_ID, departmentId: DEPARTMENT_ID }));
    const page = fixture.componentInstance as unknown as { savePlacement: () => void };

    page.savePlacement();

    const request = http.expectOne(`/api/v1/identity/people/${PERSON_ID}/placement`);

    // Sending both is what produces identity_placement_mismatch. The client does not offer the
    // server a chance to refuse it.
    expect(request.request.body.teamId).toBe(TEAM_ID);
    expect(request.request.body.departmentId).toBeNull();

    request.flush(detail({ teamId: TEAM_ID, departmentId: DEPARTMENT_ID }));
    http
      .expectOne((r) => r.url.endsWith('/teams'))
      .flush({ items: [], page: 1, pageSize: 100, totalCount: 0, totalPages: 0 });
  });

  it('disables the controls an administrator may not use on their own account', () => {
    signInAs(PERSON_ID);
    const fixture = render();
    const page = fixture.componentInstance as unknown as { isSelf: () => boolean };

    expect(page.isSelf()).toBe(true);

    // Disabled and explained rather than hidden: a control that vanishes is indistinguishable from
    // a feature that does not exist. The server refuses independently either way.
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Another administrator must make this change');
  });

  it('surfaces a refused role change instead of silently doing nothing', () => {
    signInAs('somebody-else');
    const fixture = render();
    const page = fixture.componentInstance as unknown as {
      toggleRole: (roleId: string, held: boolean) => void;
      formError: () => { code?: string } | null;
    };

    page.toggleRole('r1', true);

    http
      .expectOne(`/api/v1/identity/people/${PERSON_ID}/roles/r1`)
      .flush({ code: 'identity_last_administrator' }, { status: 409, statusText: 'Conflict' });

    fixture.detectChanges();

    expect(page.formError()?.code).toBe('identity_last_administrator');
  });
});
