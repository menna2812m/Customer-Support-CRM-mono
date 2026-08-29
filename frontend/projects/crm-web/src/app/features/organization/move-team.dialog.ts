import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { AppError } from '@crm/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { OrganizationApiService, OrganizationUnit, Team } from './organization-api.service';
import { UnitNamePipe } from './unit-name.pipe';

export interface MoveTeamDialogData {
  team: Team;

  /** Only active departments other than the team's own - the move refuses anything else. */
  departments: OrganizationUnit[];
}

/**
 * Moving a team to another department.
 *
 * Deliberately a dialog with an explicit confirmation rather than an editable field on the team,
 * because a move is not an edit to the team alone: everyone on it has their recorded department
 * changed too. The dialog reports how many people were affected once it is done, since that is the
 * part an administrator cannot see for themselves.
 */
@Component({
  selector: 'crm-move-team-dialog',
  imports: [
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatSelectModule,
    ReactiveFormsModule,
    TranslocoPipe,
    UnitNamePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>{{ 'organization.move.title' | transloco }}</h2>

    <mat-dialog-content>
      <p>{{ data.team | unitName }}</p>

      @if (error(); as failure) {
        <p role="alert">{{ 'errors.code.' + failure.code | transloco }}</p>
      }

      <form [formGroup]="form">
        <mat-form-field>
          <mat-label>{{ 'organization.move.destination' | transloco }}</mat-label>
          <mat-select formControlName="departmentId">
            @for (department of data.departments; track department.id) {
              <mat-option [value]="department.id">{{ department | unitName }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
      </form>
    </mat-dialog-content>

    <mat-dialog-actions>
      <button matButton type="button" (click)="cancel()">
        {{ 'organization.actions.cancel' | transloco }}
      </button>

      <button matButton="filled" type="button" (click)="confirm()">
        {{ 'organization.move.confirm' | transloco }}
      </button>
    </mat-dialog-actions>
  `,
})
export class MoveTeamDialog {
  private readonly api = inject(OrganizationApiService);
  private readonly formBuilder = inject(FormBuilder);
  private readonly reference = inject<MatDialogRef<MoveTeamDialog, boolean>>(MatDialogRef);

  protected readonly data = inject<MoveTeamDialogData>(MAT_DIALOG_DATA);
  protected readonly error = signal<AppError | null>(null);

  protected readonly form = this.formBuilder.nonNullable.group({
    departmentId: ['', [Validators.required]],
  });

  protected cancel(): void {
    this.reference.close(false);
  }

  protected confirm(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.error.set(null);

    this.api.moveTeam(this.data.team.id, this.form.getRawValue().departmentId).subscribe({
      next: () => this.reference.close(true),

      // A refusal here is meaningful - an inactive destination, or a name already taken there - so
      // the dialog stays open showing why rather than closing as though the move happened.
      error: (failure: AppError) => this.error.set(failure),
    });
  }
}
