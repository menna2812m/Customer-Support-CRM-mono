import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { PagedResult } from '@crm/core';

/**
 * Derived by the server from whether an identity is bound and whether the person is enabled.
 * Never stored, and never sent back.
 */
export type PersonStatus = 'invited' | 'active' | 'inactive';

/**
 * Placement with unit names in both languages, so a list shows them in the reader's language
 * without a second call - the shape feature 003 published for exactly this consumer.
 */
export interface PlacementView {
  branchId: string | null;
  branchNameAr: string | null;
  branchNameEn: string | null;
  departmentId: string | null;
  departmentNameAr: string | null;
  departmentNameEn: string | null;
  teamId: string | null;
  teamNameAr: string | null;
  teamNameEn: string | null;
}

export interface PersonSummary {
  id: string;
  displayName: string;
  email: string;
  status: PersonStatus;
  isActive: boolean;
  hasSignedIn: boolean;
  placement: PlacementView;
}

export interface RoleSummary {
  id: string;
  name: string;
}

export interface PersonDetail {
  summary: PersonSummary;
  roles: RoleSummary[];

  /** The union of what the roles grant. Shown, never edited. */
  effectivePermissions: string[];
  lastSignedInAt: string | null;
}

export interface RoleDetail extends RoleSummary {
  permissions: string[];
}

/** All three may be null, which clears the placement. */
export interface PlacementRequest {
  branchId: string | null;
  departmentId: string | null;
  teamId: string | null;
}

export interface PreProvisionRequest {
  email: string;
  displayName: string;
  roleIds?: string[];
  placement?: PlacementRequest | null;
}

/** Which people are being asked for. */
export interface PeopleListOptions {
  page?: number;
  pageSize?: number;
  search?: string;
  branchId?: string;
  departmentId?: string;
  teamId?: string;
  activeOnly?: boolean;

  /** Who has been prepared and not yet arrived. */
  unlinkedOnly?: boolean;
}

/**
 * The only place `HttpClient` appears in this feature (Constitution VI).
 */
@Injectable({ providedIn: 'root' })
export class IdentityApiService {
  private static readonly base = '/api/v1/identity';

  private readonly http = inject(HttpClient);

  listPeople(options: PeopleListOptions = {}): Observable<PagedResult<PersonSummary>> {
    return this.http.get<PagedResult<PersonSummary>>(`${IdentityApiService.base}/people`, {
      params: IdentityApiService.toParams(options),
    });
  }

  getPerson(id: string): Observable<PersonDetail> {
    return this.http.get<PersonDetail>(`${IdentityApiService.base}/people/${id}`);
  }

  preProvision(request: PreProvisionRequest): Observable<PersonDetail> {
    return this.http.post<PersonDetail>(`${IdentityApiService.base}/people`, request);
  }

  setPlacement(id: string, request: PlacementRequest): Observable<PersonDetail> {
    return this.http.put<PersonDetail>(
      `${IdentityApiService.base}/people/${id}/placement`,
      request,
    );
  }

  setActivation(id: string, isActive: boolean): Observable<PersonDetail> {
    return this.http.put<PersonDetail>(`${IdentityApiService.base}/people/${id}/activation`, {
      isActive,
    });
  }

  grantRole(id: string, roleId: string): Observable<PersonDetail> {
    return this.http.post<PersonDetail>(
      `${IdentityApiService.base}/people/${id}/roles/${roleId}`,
      null,
    );
  }

  revokeRole(id: string, roleId: string): Observable<PersonDetail> {
    return this.http.delete<PersonDetail>(
      `${IdentityApiService.base}/people/${id}/roles/${roleId}`,
    );
  }

  deletePerson(id: string): Observable<void> {
    return this.http.delete<void>(`${IdentityApiService.base}/people/${id}`);
  }

  listRoles(): Observable<RoleDetail[]> {
    return this.http.get<RoleDetail[]>(`${IdentityApiService.base}/roles`);
  }

  private static toParams(options: PeopleListOptions): HttpParams {
    let params = new HttpParams()
      .set('page', options.page ?? 1)
      .set('pageSize', options.pageSize ?? 25);

    // Only send what was asked for: the server refuses unknown parameters, and sending an empty
    // filter would narrow the list to nothing rather than leave it unfiltered.
    if (options.search) {
      params = params.set('search', options.search);
    }

    if (options.branchId) {
      params = params.set('branchId', options.branchId);
    }

    if (options.departmentId) {
      params = params.set('departmentId', options.departmentId);
    }

    if (options.teamId) {
      params = params.set('teamId', options.teamId);
    }

    if (options.activeOnly) {
      params = params.set('activeOnly', true);
    }

    if (options.unlinkedOnly) {
      params = params.set('unlinkedOnly', true);
    }

    return params;
  }
}

/**
 * A unit as a placement chooser needs it: the identifier to send, and both names so the reader
 * sees the one in their own language.
 */
export interface PlacementUnit {
  id: string;
  nameAr: string;
  nameEn: string;
}

/**
 * The organization lookups the placement form needs.
 *
 * These call the organization feature's endpoints over HTTP, which is not a cross-feature import -
 * no code crosses the boundary, only a request. Declaring the small shape this screen needs here,
 * rather than importing the other feature's model, is what keeps the boundary honest (Constitution
 * VI, and the lint rule that enforces it).
 *
 * Every lookup asks for `activeOnly=true`, because a placement may only name an active unit
 * (FR-012) and feature 003 published that parameter for exactly this consumer.
 */
@Injectable({ providedIn: 'root' })
export class PlacementLookupService {
  private static readonly base = '/api/v1/organization';

  private readonly http = inject(HttpClient);

  listBranches(): Observable<PagedResult<PlacementUnit>> {
    return this.http.get<PagedResult<PlacementUnit>>(`${PlacementLookupService.base}/branches`, {
      params: PlacementLookupService.activeOnly(),
    });
  }

  listDepartments(): Observable<PagedResult<PlacementUnit>> {
    return this.http.get<PagedResult<PlacementUnit>>(`${PlacementLookupService.base}/departments`, {
      params: PlacementLookupService.activeOnly(),
    });
  }

  listTeams(departmentId: string): Observable<PagedResult<PlacementUnit>> {
    return this.http.get<PagedResult<PlacementUnit>>(
      `${PlacementLookupService.base}/departments/${departmentId}/teams`,
      { params: PlacementLookupService.activeOnly() },
    );
  }

  private static activeOnly(): HttpParams {
    return new HttpParams().set('page', 1).set('pageSize', 100).set('activeOnly', true);
  }
}
