import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

/** Mirrors the shared pagination contract (contracts/pagination-contract.md). */
export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface DiagnosticItem {
  id: string;
  name: string;
  createdAt: string;
}

export interface EchoRequest {
  message: string;
  repeatCount: number;
}

export interface EchoResponse {
  message: string;
  receivedAt: string;
  correlationId: string;
}

/**
 * The only HTTP caller for the diagnostics feature (Constitution VI, spec FR-029).
 * A lint rule prevents any component from injecting HttpClient directly.
 */
@Injectable({ providedIn: 'root' })
export class DiagnosticsApiService {
  private readonly http = inject(HttpClient);

  private static readonly base = '/api/v1/diagnostics';

  listItems(
    page: number,
    pageSize: number,
    sort?: string,
  ): Observable<PagedResult<DiagnosticItem>> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);

    if (sort) {
      params = params.set('sort', sort);
    }

    return this.http.get<PagedResult<DiagnosticItem>>(`${DiagnosticsApiService.base}/items`, {
      params,
    });
  }

  echo(request: EchoRequest): Observable<EchoResponse> {
    return this.http.post<EchoResponse>(`${DiagnosticsApiService.base}/echo`, request);
  }
}
