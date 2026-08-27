import { ChangeDetectionStrategy, Component, inject, OnInit } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { TranslocoPipe } from '@jsverse/transloco';
import { MatCardModule } from '@angular/material/card';
import { RequestSignal } from '@crm/core';
import { StateContainerComponent } from '@crm/ui';
import { HealthApiService, HealthReport } from './health-api.service';

/**
 * Home screen. It exists to prove the platform end to end (spec US1): the frontend reaches the
 * API through the configured base URL, and every failure mode renders a mandated state rather
 * than a blank page.
 */
@Component({
  selector: 'crm-home-page',
  imports: [DecimalPipe, MatCardModule, StateContainerComponent, TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1 class="crm-home__title">{{ 'home.title' | transloco }}</h1>

    <crm-state-container [state]="health.value()" (retry)="load()">
      <mat-card appearance="outlined">
        <mat-card-content>
          <p class="crm-home__status">
            {{ 'home.apiReports' | transloco }} <strong>{{ health.data()?.status }}</strong>
          </p>

          <ul class="crm-home__checks">
            @for (check of health.data()?.checks ?? []; track check.name) {
              <li>
                {{ check.name }}: {{ check.status }}
                <span class="crm-home__duration"
                  >({{ check.durationMs | number: '1.0-0' }} ms)</span
                >
              </li>
            }
          </ul>
        </mat-card-content>
      </mat-card>
    </crm-state-container>
  `,
  styles: `
    .crm-home__title {
      font: var(--mat-sys-headline-small);
      margin-block-end: var(--crm-space-md);
    }

    .crm-home__checks {
      margin-block: var(--crm-space-sm) 0;
      padding-inline-start: var(--crm-space-lg);
    }

    .crm-home__duration {
      color: var(--mat-sys-on-surface-variant);
    }
  `,
})
export class HomePage implements OnInit {
  private readonly api = inject(HealthApiService);

  protected readonly health = new RequestSignal<HealthReport>();

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.health.setLoading();

    this.api.readReadiness().subscribe({
      next: (report) => this.health.setSuccess(report, () => false),
      error: (error) => this.health.setError(error),
    });
  }
}
