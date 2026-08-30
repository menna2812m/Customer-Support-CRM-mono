import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
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
import {
  OrganizationApiService,
  OrganizationUnit,
  PagedResult,
  Team,
  TeamMoveResult,
} from './organization-api.service';
import { MoveTeamDialog, MoveTeamDialogData } from './move-team.dialog';

/**
 * Departments, and the teams inside them.
 *
 * Teams are managed from within a department rather than as a separate top-level list. That is the
 * containment rule made visible: creating a team starts from the department it will belong to, so
 * the form never opens with an empty department dropdown somebody has to remember to fill.
 */
@Component({
  selector: 'crm-departments-page',
  imports: [
    BadgeComponent,
    CodeComponent,
    MatButtonModule,
    MatDialogModule,
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
  templateUrl: './departments.page.html',
  styles: `
    /* The expanded teams region. Inset on the leading edge and on a sunken surface, so the nesting
       is visible without a second panel border - and it leans the correct way in both directions
       because the padding is logical. */
    .crm-org__teams-row > td {
      padding: 0;
      background: var(--crm-surface-sunken);
    }

    .crm-org__teams {
      padding-inline: var(--crm-space-4);
      padding-block: var(--crm-space-4);
      border-inline-start: 3px solid var(--crm-accent);
    }

    .crm-org__teams-title {
      margin-block-end: var(--crm-space-3);
      color: var(--crm-ink-secondary);
      font-size: var(--crm-text-xs);
      font-weight: var(--crm-weight-semibold);
      letter-spacing: var(--crm-tracking-wide);
      text-transform: uppercase;
    }

    .crm-org__team-form {
      margin-block-start: var(--crm-space-4);
      padding-block-start: var(--crm-space-4);
      border-block-start: var(--crm-border-width) solid var(--crm-border);
    }

    /* The department name doubles as the control that reveals its teams. Styled as text rather
       than as a button so the table still reads as a table. */
    .crm-org__disclosure {
      padding: 0;
      border: 0;
      background: none;
      font: inherit;
      font-weight: var(--crm-weight-medium);
      color: var(--crm-accent);
      cursor: pointer;
      text-align: start;
    }

    .crm-org__disclosure:hover {
      text-decoration: underline;
    }
  `,
})
export class DepartmentsPage implements OnInit {
  private readonly api = inject(OrganizationApiService);
  private readonly formBuilder = inject(FormBuilder);
  private readonly dialog = inject(MatDialog);

  protected readonly departments = new RequestSignal<PagedResult<OrganizationUnit>>();
  protected readonly teams = new RequestSignal<PagedResult<Team>>();

  /** Which department's teams are open. Null means none - the list is collapsed. */
  protected readonly openDepartment = signal<OrganizationUnit | null>(null);
  protected readonly formError = signal<AppError | null>(null);
  protected readonly moveOutcome = signal<TeamMoveResult | null>(null);

  protected readonly form = this.formBuilder.nonNullable.group({
    nameAr: ['', [Validators.required, Validators.maxLength(200)]],
    nameEn: ['', [Validators.required, Validators.maxLength(200)]],
    code: ['', [Validators.required, Validators.maxLength(32)]],
  });

  /** Teams get their own form, because the two are filled in at different moments. */
  protected readonly teamForm = this.formBuilder.nonNullable.group({
    nameAr: ['', [Validators.required, Validators.maxLength(200)]],
    nameEn: ['', [Validators.required, Validators.maxLength(200)]],
    code: ['', [Validators.required, Validators.maxLength(32)]],
  });

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.departments.setLoading();

    this.api.listDepartments({ pageSize: 100 }).subscribe({
      next: (result) => this.departments.setSuccess(result, (value) => value.items.length === 0),
      error: (error: AppError) => this.departments.setError(error),
    });
  }

  protected create(): void {
    this.formError.set(null);

    // Client-side rules are a convenience; the server is the authority (Constitution IV).
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.api.createDepartment(this.form.getRawValue()).subscribe({
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

  protected toggleActivation(department: OrganizationUnit): void {
    this.api.setDepartmentActivation(department.id, !department.isActive).subscribe({
      next: () => this.load(),
      error: (error: AppError) => this.departments.setError(error),
    });
  }

  protected remove(department: OrganizationUnit): void {
    this.api.deleteDepartment(department.id).subscribe({
      next: () => {
        if (this.openDepartment()?.id === department.id) {
          this.openDepartment.set(null);
        }

        this.load();
      },
      // A refused delete names what depends on the unit, so the error is worth showing rather
      // than swallowing (spec FR-012).
      error: (error: AppError) => this.formError.set(error),
    });
  }

  protected openTeams(department: OrganizationUnit): void {
    if (this.openDepartment()?.id === department.id) {
      this.openDepartment.set(null);
      return;
    }

    this.openDepartment.set(department);
    this.loadTeams(department.id);
  }

  protected loadTeams(departmentId: string): void {
    this.teams.setLoading();

    this.api.listTeams(departmentId, { pageSize: 100 }).subscribe({
      next: (result) => this.teams.setSuccess(result, (value) => value.items.length === 0),
      error: (error: AppError) => this.teams.setError(error),
    });
  }

  protected createTeam(): void {
    const department = this.openDepartment();

    if (!department) {
      return;
    }

    this.formError.set(null);

    if (this.teamForm.invalid) {
      this.teamForm.markAllAsTouched();
      return;
    }

    this.api.createTeam(department.id, this.teamForm.getRawValue()).subscribe({
      next: () => {
        this.teamForm.reset();
        this.loadTeams(department.id);
      },
      error: (error: AppError) => {
        this.formError.set(error);
        applyServerErrors(this.teamForm, error);
      },
    });
  }

  protected toggleTeamActivation(team: Team): void {
    this.api.setTeamActivation(team.id, !team.isActive).subscribe({
      next: () => this.loadTeams(team.departmentId),
      error: (error: AppError) => this.formError.set(error),
    });
  }

  protected removeTeam(team: Team): void {
    this.api.deleteTeam(team.id).subscribe({
      next: () => this.loadTeams(team.departmentId),
      error: (error: AppError) => this.formError.set(error),
    });
  }

  /**
   * Moving is an explicit action rather than an editable field, because it changes other people's
   * records: everyone on the team has their recorded department changed with it.
   */
  protected move(team: Team): void {
    const data: MoveTeamDialogData = {
      team,
      departments: (this.departments.data()?.items ?? []).filter(
        (department) => department.isActive && department.id !== team.departmentId,
      ),
    };

    this.dialog
      .open(MoveTeamDialog, { data })
      .afterClosed()
      .subscribe((result: TeamMoveResult | undefined) => {
        if (!result) {
          return;
        }

        // How many people were reassigned is the part of the move nobody can verify by looking at
        // the screen, so it is reported rather than discarded (spec FR-015).
        this.moveOutcome.set(result);
        this.loadTeams(team.departmentId);
        this.load();
      });
  }

  protected serverCodes(field: string): string[] {
    return serverErrorCodes(this.form, field);
  }

  protected teamServerCodes(field: string): string[] {
    return serverErrorCodes(this.teamForm, field);
  }
}
