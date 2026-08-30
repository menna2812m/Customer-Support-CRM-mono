import { ChangeDetectionStrategy, Component, inject, OnInit } from '@angular/core';
import { DecimalPipe } from '@angular/common';

import { RequestSignal } from '@crm/core';
import {
  BadgeComponent,
  PageHeaderComponent,
  PanelComponent,
  StateContainerComponent,
} from '@crm/ui';
import { HealthApiService, HealthReport } from './health-api.service';

/**
 * Home screen. It exists to prove the platform end to end (spec US1): the frontend reaches the
 * API through the configured base URL, and every failure mode renders a mandated state rather
 * than a blank page.
 */
@Component({
  selector: 'crm-home-page',
  imports: [
    BadgeComponent,
    DecimalPipe,
    PageHeaderComponent,
    PanelComponent,
    StateContainerComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <crm-page-header titleKey="home.title" descriptionKey="home.subtitle" />

    <crm-state-container [state]="health.value()" (retry)="load()">
      <crm-panel titleKey="home.apiReports">
        <div crmPanelActions>
          <crm-badge [tone]="health.data()?.status === 'Healthy' ? 'success' : 'danger'">
            {{ health.data()?.status }}
          </crm-badge>
        </div>

        <dl class="crm-home__checks">
          @for (check of health.data()?.checks ?? []; track check.name) {
            <div class="crm-home__check">
              <dt class="crm-home__check-name">{{ check.name }}</dt>
              <dd class="crm-home__check-value">
                <crm-badge [tone]="check.status === 'Healthy' ? 'success' : 'danger'">
                  {{ check.status }}
                </crm-badge>
                <span class="crm-home__duration">{{ check.durationMs | number: '1.0-0' }} ms</span>
              </dd>
            </div>
          }
        </dl>
      </crm-panel>
    </crm-state-container>
  `,
  styles: `
    /* A description list, because each check genuinely is a name and its value - and it gives the
       relationship to assistive technology for free. */
    .crm-home__checks {
      display: flex;
      flex-direction: column;
      margin: 0;
    }

    .crm-home__check {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: var(--crm-space-3);

      padding-block: var(--crm-space-3);
      border-block-end: var(--crm-border-width) solid var(--crm-border);
    }

    .crm-home__check:last-child {
      border-block-end: none;
      padding-block-end: 0;
    }

    .crm-home__check:first-child {
      padding-block-start: 0;
    }

    .crm-home__check-name {
      font-weight: var(--crm-weight-medium);
    }

    .crm-home__check-value {
      display: flex;
      align-items: center;
      gap: var(--crm-space-2);
      margin: 0;
    }

    .crm-home__duration {
      color: var(--crm-ink-muted);
      font-family: var(--crm-font-mono);
      font-size: var(--crm-text-xs);
      font-variant-numeric: tabular-nums;
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
