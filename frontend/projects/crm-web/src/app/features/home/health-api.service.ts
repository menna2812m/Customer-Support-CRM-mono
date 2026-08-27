import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

/** Health report as published by the API (contracts/foundation-api.yaml). */
export interface HealthReport {
  status: 'Healthy' | 'Degraded' | 'Unhealthy';
  checks: HealthCheckResult[];
}

export interface HealthCheckResult {
  name: string;
  status: 'Healthy' | 'Degraded' | 'Unhealthy';
  durationMs: number;
}

/**
 * The only HTTP caller for the home feature (Constitution VI, spec FR-029).
 * Components inject this; they never inject HttpClient, and a lint rule enforces that.
 */
@Injectable({ providedIn: 'root' })
export class HealthApiService {
  private readonly http = inject(HttpClient);

  /** Readiness includes dependency checks; liveness only reports that the process is up. */
  readReadiness(): Observable<HealthReport> {
    return this.http.get<HealthReport>('/health/ready');
  }
}
