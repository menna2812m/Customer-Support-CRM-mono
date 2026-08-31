import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { FormBuilder, FormGroupDirective, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { RouterLink } from '@angular/router';
import {
  AppError,
  PagedResult,
  RequestSignal,
  applyServerErrors,
  serverErrorCodes,
} from '@crm/core';
import {
  BadgeComponent,
  BadgeTone,
  BilingualName,
  NoticeComponent,
  PageHeaderComponent,
  PanelComponent,
  StateContainerComponent,
  UnitNamePipe,
} from '@crm/ui';
import { TranslocoPipe } from '@jsverse/transloco';
import { IdentityApiService, PersonSummary, PersonStatus } from './identity-api.service';

/**
 * Everyone who may sign in, and everyone prepared to.
 *
 * Status carries three values rather than one boolean, because two facts - whether an identity is
 * bound, and whether the account is enabled - describe a state that neither expresses alone.
 */
@Component({
  selector: 'crm-people-page',
  imports: [
    BadgeComponent,
    MatButtonModule,
    MatCheckboxModule,
    MatFormFieldModule,
    MatInputModule,
    NoticeComponent,
    PageHeaderComponent,
    PanelComponent,
    ReactiveFormsModule,
    RouterLink,
    StateContainerComponent,
    TranslocoPipe,
    UnitNamePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './people.page.html',
})
export class PeoplePage implements OnInit {
  private readonly api = inject(IdentityApiService);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly people = new RequestSignal<PagedResult<PersonSummary>>();
  protected readonly formError = signal<AppError | null>(null);

  protected readonly search = signal('');
  protected readonly activeOnly = signal(false);
  protected readonly unlinkedOnly = signal(false);

  private readonly formDirective = viewChild(FormGroupDirective);

  /** Both names are not asked for here: the provider owns a person's name (spec FR-004). */
  protected readonly form = this.formBuilder.nonNullable.group({
    email: ['', [Validators.required, Validators.email, Validators.maxLength(256)]],
    displayName: ['', [Validators.required, Validators.maxLength(200)]],
  });

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.people.setLoading();

    this.api
      .listPeople({
        pageSize: 100,
        search: this.search() || undefined,
        activeOnly: this.activeOnly() || undefined,
        unlinkedOnly: this.unlinkedOnly() || undefined,
      })
      .subscribe({
        next: (result) => this.people.setSuccess(result, (value) => value.items.length === 0),
        error: (error: AppError) => this.people.setError(error),
      });
  }

  protected applySearch(value: string): void {
    this.search.set(value);
    this.load();
  }

  protected toggleActiveOnly(checked: boolean): void {
    this.activeOnly.set(checked);
    this.load();
  }

  protected toggleUnlinkedOnly(checked: boolean): void {
    this.unlinkedOnly.set(checked);
    this.load();
  }

  protected create(): void {
    this.formError.set(null);

    // Client-side rules are a convenience; the server is the authority (Constitution IV).
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.api.preProvision(this.form.getRawValue()).subscribe({
      next: () => {
        // resetForm rather than reset. Resetting the group alone leaves the directive marked as
        // submitted, and Material's default error matcher treats a submitted form as touched - so
        // both required fields render as errors the instant a creation succeeds, which reads as a
        // failure of the thing that just worked.
        this.formDirective()?.resetForm();
        this.load();
      },
      error: (error: AppError) => {
        this.formError.set(error);
        applyServerErrors(this.form, error);
      },
    });
  }

  protected serverCodes(field: string): string[] {
    return serverErrorCodes(this.form, field);
  }

  /**
   * The department as a bilingual name, or null when the person is not placed in one.
   *
   * Returned as a structural value rather than reaching for one language, so the shared pipe
   * decides which name the reader sees (spec LR-003).
   */
  protected departmentName(person: PersonSummary): BilingualName | null {
    return person.placement.departmentId
      ? {
          nameAr: person.placement.departmentNameAr ?? '',
          nameEn: person.placement.departmentNameEn ?? '',
        }
      : null;
  }

  /** Invited is neutral rather than a warning: being prepared is a normal state, not a problem. */
  protected tone(status: PersonStatus): BadgeTone {
    switch (status) {
      case 'active':
        return 'success';
      case 'invited':
        return 'info';
      default:
        return 'neutral';
    }
  }
}
