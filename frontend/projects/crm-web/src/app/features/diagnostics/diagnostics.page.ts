import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import {
  AppError,
  LanguageService,
  RequestSignal,
  applyServerErrors,
  serverErrorCodes,
} from '@crm/core';
import { StateContainerComponent } from '@crm/ui';
import { TranslocoPipe } from '@jsverse/transloco';
import {
  DiagnosticItem,
  DiagnosticsApiService,
  EchoResponse,
  PagedResult,
} from './diagnostics-api.service';

/**
 * The reference screen (spec FR-051). It is the worked example a real feature is copied from:
 * a list bound to the pagination contract, a typed reactive form whose server-side validation
 * failures land on the right fields, and every mandated state handled by crm-state-container.
 */
@Component({
  selector: 'crm-diagnostics-page',
  imports: [
    DatePipe,
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    ReactiveFormsModule,
    StateContainerComponent,
    TranslocoPipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './diagnostics.page.html',
  styles: `
    .crm-diagnostics__section {
      margin-block-end: var(--crm-space-lg);
    }

    .crm-diagnostics__pager {
      display: flex;
      align-items: center;
      gap: var(--crm-space-sm);
      margin-block-start: var(--crm-space-md);
    }

    .crm-diagnostics__form {
      display: flex;
      flex-wrap: wrap;
      gap: var(--crm-space-md);
      align-items: start;
    }
  `,
})
export class DiagnosticsPage implements OnInit {
  private readonly api = inject(DiagnosticsApiService);
  private readonly formBuilder = inject(FormBuilder);
  private readonly languages = inject(LanguageService);

  /** Dates and numbers follow the active language (spec FR-038). */
  protected readonly locale = this.languages.language;

  protected readonly pageSize = 10;
  protected readonly page = signal(1);
  protected readonly items = new RequestSignal<PagedResult<DiagnosticItem>>();
  protected readonly echoResult = signal<EchoResponse | null>(null);
  protected readonly echoError = signal<AppError | null>(null);

  protected readonly form = this.formBuilder.nonNullable.group({
    message: ['', [Validators.required, Validators.maxLength(200)]],
    repeatCount: [1, [Validators.required, Validators.min(1), Validators.max(10)]],
  });

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.items.setLoading();

    this.api.listItems(this.page(), this.pageSize).subscribe({
      next: (result) => this.items.setSuccess(result),
      error: (error: AppError) => this.items.setError(error),
    });
  }

  protected goTo(page: number): void {
    const total = this.items.data()?.totalPages ?? 1;

    if (page < 1 || page > total) {
      return;
    }

    this.page.set(page);
    this.load();
  }

  protected submit(): void {
    this.echoResult.set(null);
    this.echoError.set(null);

    // Client-side rules are a convenience; the server is the authority (Constitution IV).
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.api.echo(this.form.getRawValue()).subscribe({
      next: (response) => this.echoResult.set(response),
      error: (error: AppError) => {
        this.echoError.set(error);
        applyServerErrors(this.form, error);
      },
    });
  }

  protected serverCodes(field: string): string[] {
    return serverErrorCodes(this.form, field);
  }
}
