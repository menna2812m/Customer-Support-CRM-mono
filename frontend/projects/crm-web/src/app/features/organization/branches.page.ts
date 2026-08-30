import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { AppError, RequestSignal, applyServerErrors, serverErrorCodes } from '@crm/core';
import {
  BadgeComponent,
  CodeComponent,
  PageHeaderComponent,
  PanelComponent,
  StateContainerComponent,
  NoticeComponent,
  UnitNamePipe,
} from '@crm/ui';
import { TranslocoPipe } from '@jsverse/transloco';
import { OrganizationApiService, OrganizationUnit, PagedResult } from './organization-api.service';

/**
 * Branches: geography, and the simpler half of the model. A branch contains nothing and belongs to
 * nothing, so this screen is the department one without the nesting.
 */
@Component({
  selector: 'crm-branches-page',
  imports: [
    BadgeComponent,
    CodeComponent,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    PageHeaderComponent,
    PanelComponent,
    ReactiveFormsModule,
    StateContainerComponent,
    TranslocoPipe,
    UnitNamePipe,
    NoticeComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './branches.page.html',
})
export class BranchesPage implements OnInit {
  private readonly api = inject(OrganizationApiService);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly branches = new RequestSignal<PagedResult<OrganizationUnit>>();
  protected readonly formError = signal<AppError | null>(null);

  protected readonly form = this.formBuilder.nonNullable.group({
    nameAr: ['', [Validators.required, Validators.maxLength(200)]],
    nameEn: ['', [Validators.required, Validators.maxLength(200)]],
    code: ['', [Validators.required, Validators.maxLength(32)]],
  });

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.branches.setLoading();

    this.api.listBranches({ pageSize: 100 }).subscribe({
      next: (result) => this.branches.setSuccess(result, (value) => value.items.length === 0),
      error: (error: AppError) => this.branches.setError(error),
    });
  }

  protected create(): void {
    this.formError.set(null);

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.api.createBranch(this.form.getRawValue()).subscribe({
      next: () => {
        this.form.reset();
        this.load();
      },
      error: (error: AppError) => {
        this.formError.set(error);
        applyServerErrors(this.form, error);
      },
    });
  }

  protected toggleActivation(branch: OrganizationUnit): void {
    this.api.setBranchActivation(branch.id, !branch.isActive).subscribe({
      next: () => this.load(),
      error: (error: AppError) => this.formError.set(error),
    });
  }

  protected remove(branch: OrganizationUnit): void {
    this.api.deleteBranch(branch.id).subscribe({
      next: () => this.load(),

      // The refusal counts the people placed in the branch, which is what makes it actionable.
      error: (error: AppError) => this.formError.set(error),
    });
  }

  protected serverCodes(field: string): string[] {
    return serverErrorCodes(this.form, field);
  }
}
