import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideCrmTesting } from '@crm/core/testing';
import { IdentityApiService, PlacementLookupService } from './identity-api.service';

/**
 * The feature's only HTTP surface. What is worth testing is the shape of the requests it builds:
 * the server refuses unknown parameters, and an empty filter sent as a value narrows a list to
 * nothing rather than leaving it unfiltered.
 */
describe('IdentityApiService', () => {
  let http: HttpTestingController;
  let api: IdentityApiService;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideCrmTesting()] });

    http = TestBed.inject(HttpTestingController);
    api = TestBed.inject(IdentityApiService);
  });

  afterEach(() => http.verify());

  it('sends only the filters that were asked for', () => {
    api.listPeople({ search: 'layla' }).subscribe();

    const request = http.expectOne((r) => r.url === '/api/v1/identity/people');

    expect(request.request.params.get('search')).toBe('layla');

    // Absent rather than empty: an empty departmentId would be a filter matching nothing, and an
    // unknown parameter is refused outright by the server.
    expect(request.request.params.has('departmentId')).toBe(false);
    expect(request.request.params.has('activeOnly')).toBe(false);

    request.flush({ items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0 });
  });

  it('always sends paging, because the contract requires it', () => {
    api.listPeople().subscribe();

    const request = http.expectOne((r) => r.url === '/api/v1/identity/people');

    expect(request.request.params.get('page')).toBe('1');
    expect(request.request.params.get('pageSize')).toBe('25');

    request.flush({ items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0 });
  });

  it('grants a role by posting to the role address rather than sending a list', () => {
    api.grantRole('p1', 'r1').subscribe();

    const request = http.expectOne('/api/v1/identity/people/p1/roles/r1');

    // One role per call: a refusal then names the role it refused rather than failing a batch
    // nobody can interpret.
    expect(request.request.method).toBe('POST');

    request.flush({});
  });

  it('revokes a role with a delete to the same address', () => {
    api.revokeRole('p1', 'r1').subscribe();

    const request = http.expectOne('/api/v1/identity/people/p1/roles/r1');
    expect(request.request.method).toBe('DELETE');

    request.flush({});
  });

  it('sends placement as one request carrying all three ids', () => {
    api.setPlacement('p1', { branchId: 'b1', departmentId: null, teamId: 't1' }).subscribe();

    const request = http.expectOne('/api/v1/identity/people/p1/placement');

    // One operation, because the three carry an invariant between them. Three separate calls would
    // pass through a state violating it.
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ branchId: 'b1', departmentId: null, teamId: 't1' });

    request.flush({});
  });
});

/**
 * The organization lookups the placement chooser needs.
 *
 * They cross a feature boundary by HTTP rather than by import, which is the distinction Constitution
 * VI draws: a request may cross, a type may not.
 */
describe('PlacementLookupService', () => {
  let http: HttpTestingController;
  let lookups: PlacementLookupService;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideCrmTesting()] });

    http = TestBed.inject(HttpTestingController);
    lookups = TestBed.inject(PlacementLookupService);
  });

  afterEach(() => http.verify());

  it('asks for active units only, on every lookup', () => {
    lookups.listBranches().subscribe();
    lookups.listDepartments().subscribe();
    lookups.listTeams('d1').subscribe();

    for (const url of [
      '/api/v1/organization/branches',
      '/api/v1/organization/departments',
      '/api/v1/organization/departments/d1/teams',
    ]) {
      const request = http.expectOne((r) => r.url === url);

      // A placement may only name an active unit (FR-012). Feature 003 published this parameter for
      // exactly this consumer, so the filtering happens once on the server rather than in every
      // chooser that forgets to.
      expect(request.request.params.get('activeOnly')).toBe('true');

      request.flush({ items: [], page: 1, pageSize: 100, totalCount: 0, totalPages: 0 });
    }
  });
});
