import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { OrganizationApiService } from './organization-api.service';

/**
 * The one place this feature speaks HTTP, so the shape of every request is asserted here rather
 * than through a component.
 */
describe('OrganizationApiService', () => {
  let api: OrganizationApiService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    api = TestBed.inject(OrganizationApiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('omits activeOnly unless it is asked for', () => {
    // The endpoints reject unknown query parameters, and a default false would be noise on every
    // request. Sending it only when true keeps the common call clean.
    api.listDepartments().subscribe();

    const request = http.expectOne(
      (candidate) => candidate.url === '/api/v1/organization/departments',
    );

    expect(request.request.params.has('activeOnly')).toBe(false);
    request.flush({ items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0 });
  });

  it('sends activeOnly when a placement chooser asks for it', () => {
    api.listBranches({ activeOnly: true }).subscribe();

    const request = http.expectOne((candidate) => candidate.params.get('activeOnly') === 'true');

    expect(request.request.method).toBe('GET');
    request.flush({ items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0 });
  });

  it('creates a team under its department rather than at the top level', () => {
    // The containment rule reaches the URL: there is no route that creates a team without one.
    api.createTeam('dept-1', { nameAr: 'فريق', nameEn: 'Team', code: 'T1' }).subscribe();

    const request = http.expectOne('/api/v1/organization/departments/dept-1/teams');

    expect(request.request.method).toBe('POST');
    request.flush({});
  });

  it('sends no code when renaming, because a code never changes', () => {
    api.renameDepartment('dept-1', { nameAr: 'جديد', nameEn: 'New' }).subscribe();

    const request = http.expectOne('/api/v1/organization/departments/dept-1');

    expect(request.request.method).toBe('PUT');
    expect(Object.keys(request.request.body as object)).toEqual(['nameAr', 'nameEn']);
    request.flush({});
  });

  it('moves a team by putting its destination department', () => {
    api.moveTeam('team-1', 'dept-2').subscribe();

    const request = http.expectOne('/api/v1/organization/teams/team-1/department');

    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ departmentId: 'dept-2' });
    request.flush({ team: {}, membersReassigned: 3 });
  });
});
