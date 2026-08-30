import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { PagedResult } from '@crm/core';

export type { PagedResult };

/** A branch or a department. Both carry the same shape; only teams add anything. */
export interface OrganizationUnit {
  id: string;
  nameAr: string;
  nameEn: string;
  code: string;
  isActive: boolean;
}

/**
 * A team always carries its department, so a placement chooser can show
 * "Technical Support / Tier 1" without a second call.
 */
export interface Team extends OrganizationUnit {
  departmentId: string;
  departmentNameAr: string;
  departmentNameEn: string;
}

export interface CreateUnitRequest {
  nameAr: string;
  nameEn: string;
  code: string;
}

/** Renaming carries no code: a code is set once and never changes (spec FR-006). */
export interface RenameUnitRequest {
  nameAr: string;
  nameEn: string;
}

export interface TeamMoveResult {
  team: Team;
  membersReassigned: number;
}

/** Which list is being asked for. `activeOnly` is what a placement chooser sends. */
export interface UnitListOptions {
  page?: number;
  pageSize?: number;
  sort?: string;
  search?: string;
  activeOnly?: boolean;
}

/**
 * The only HTTP caller for the organization feature (Constitution VI). A lint rule prevents any
 * component from injecting HttpClient directly.
 */
@Injectable({ providedIn: 'root' })
export class OrganizationApiService {
  private readonly http = inject(HttpClient);

  private static readonly base = '/api/v1/organization';

  listBranches(options: UnitListOptions = {}): Observable<PagedResult<OrganizationUnit>> {
    return this.http.get<PagedResult<OrganizationUnit>>(`${OrganizationApiService.base}/branches`, {
      params: OrganizationApiService.toParams(options),
    });
  }

  createBranch(request: CreateUnitRequest): Observable<OrganizationUnit> {
    return this.http.post<OrganizationUnit>(`${OrganizationApiService.base}/branches`, request);
  }

  renameBranch(id: string, request: RenameUnitRequest): Observable<OrganizationUnit> {
    return this.http.put<OrganizationUnit>(
      `${OrganizationApiService.base}/branches/${id}`,
      request,
    );
  }

  setBranchActivation(id: string, isActive: boolean): Observable<OrganizationUnit> {
    return this.http.put<OrganizationUnit>(
      `${OrganizationApiService.base}/branches/${id}/activation`,
      {
        isActive,
      },
    );
  }

  deleteBranch(id: string): Observable<void> {
    return this.http.delete<void>(`${OrganizationApiService.base}/branches/${id}`);
  }

  listDepartments(options: UnitListOptions = {}): Observable<PagedResult<OrganizationUnit>> {
    return this.http.get<PagedResult<OrganizationUnit>>(
      `${OrganizationApiService.base}/departments`,
      { params: OrganizationApiService.toParams(options) },
    );
  }

  getDepartment(id: string): Observable<OrganizationUnit> {
    return this.http.get<OrganizationUnit>(`${OrganizationApiService.base}/departments/${id}`);
  }

  createDepartment(request: CreateUnitRequest): Observable<OrganizationUnit> {
    return this.http.post<OrganizationUnit>(`${OrganizationApiService.base}/departments`, request);
  }

  renameDepartment(id: string, request: RenameUnitRequest): Observable<OrganizationUnit> {
    return this.http.put<OrganizationUnit>(
      `${OrganizationApiService.base}/departments/${id}`,
      request,
    );
  }

  setDepartmentActivation(id: string, isActive: boolean): Observable<OrganizationUnit> {
    return this.http.put<OrganizationUnit>(
      `${OrganizationApiService.base}/departments/${id}/activation`,
      { isActive },
    );
  }

  deleteDepartment(id: string): Observable<void> {
    return this.http.delete<void>(`${OrganizationApiService.base}/departments/${id}`);
  }

  /**
   * Teams are listed and created under their department, never at the top level. That is the
   * containment rule made visible: a team created here cannot be missing one.
   */
  listTeams(departmentId: string, options: UnitListOptions = {}): Observable<PagedResult<Team>> {
    return this.http.get<PagedResult<Team>>(
      `${OrganizationApiService.base}/departments/${departmentId}/teams`,
      { params: OrganizationApiService.toParams(options) },
    );
  }

  createTeam(departmentId: string, request: CreateUnitRequest): Observable<Team> {
    return this.http.post<Team>(
      `${OrganizationApiService.base}/departments/${departmentId}/teams`,
      request,
    );
  }

  renameTeam(id: string, request: RenameUnitRequest): Observable<Team> {
    return this.http.put<Team>(`${OrganizationApiService.base}/teams/${id}`, request);
  }

  setTeamActivation(id: string, isActive: boolean): Observable<Team> {
    return this.http.put<Team>(`${OrganizationApiService.base}/teams/${id}/activation`, {
      isActive,
    });
  }

  /** Moves a team, carrying its members with it. The result reports how many were affected. */
  moveTeam(id: string, departmentId: string): Observable<TeamMoveResult> {
    return this.http.put<TeamMoveResult>(`${OrganizationApiService.base}/teams/${id}/department`, {
      departmentId,
    });
  }

  deleteTeam(id: string): Observable<void> {
    return this.http.delete<void>(`${OrganizationApiService.base}/teams/${id}`);
  }

  private static toParams(options: UnitListOptions): HttpParams {
    let params = new HttpParams()
      .set('page', options.page ?? 1)
      .set('pageSize', options.pageSize ?? 25);

    if (options.sort) {
      params = params.set('sort', options.sort);
    }

    if (options.search) {
      params = params.set('search', options.search);
    }

    // Sent only when true: the endpoint rejects unknown query parameters, and a default false is
    // noise on every request.
    if (options.activeOnly) {
      params = params.set('activeOnly', true);
    }

    return params;
  }
}
