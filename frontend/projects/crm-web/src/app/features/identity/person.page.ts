import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { AppError, AuthSession, RequestSignal } from '@crm/core';
import {
  BadgeComponent,
  BadgeTone,
  CodeComponent,
  NoticeComponent,
  PageHeaderComponent,
  PanelComponent,
  StateContainerComponent,
  UnitNamePipe,
} from '@crm/ui';
import { TranslocoPipe } from '@jsverse/transloco';
import {
  IdentityApiService,
  PersonDetail,
  PersonStatus,
  PlacementLookupService,
  PlacementUnit,
  RoleDetail,
} from './identity-api.service';

/**
 * One person: who they are, what they may do, and where they sit.
 *
 * Three blocks, kept visually distinct on purpose. Identity is read-only because the provider owns
 * it. Roles are the input; effective permissions are the consequence, shown beneath them and never
 * editable - rendering the two alike would invite somebody to try editing a permission.
 */
@Component({
  selector: 'crm-person-page',
  imports: [
    BadgeComponent,
    CodeComponent,
    MatButtonModule,
    MatCheckboxModule,
    MatFormFieldModule,
    MatSelectModule,
    NoticeComponent,
    PageHeaderComponent,
    PanelComponent,
    ReactiveFormsModule,
    StateContainerComponent,
    TranslocoPipe,
    UnitNamePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './person.page.html',
})
export class PersonPage implements OnInit {
  private readonly api = inject(IdentityApiService);
  private readonly lookups = inject(PlacementLookupService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly session = inject(AuthSession);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly person = new RequestSignal<PersonDetail>();
  protected readonly roles = signal<RoleDetail[]>([]);
  protected readonly formError = signal<AppError | null>(null);
  protected readonly confirmingDelete = signal(false);

  protected readonly branches = signal<PlacementUnit[]>([]);
  protected readonly departments = signal<PlacementUnit[]>([]);
  protected readonly teams = signal<PlacementUnit[]>([]);

  protected readonly placement = this.formBuilder.nonNullable.group({
    branchId: [''],
    departmentId: [''],
    teamId: [''],
  });

  private personId = '';

  ngOnInit(): void {
    this.personId = this.route.snapshot.paramMap.get('id') ?? '';

    this.load();

    this.api.listRoles().subscribe({ next: (roles) => this.roles.set(roles) });
    this.lookups.listBranches().subscribe({ next: (page) => this.branches.set(page.items) });
    this.lookups.listDepartments().subscribe({ next: (page) => this.departments.set(page.items) });
  }

  protected load(): void {
    this.person.setLoading();

    this.api.getPerson(this.personId).subscribe({
      next: (person) => {
        this.person.setSuccess(person);
        this.applyPlacement(person);
      },
      error: (error: AppError) => this.person.setError(error),
    });
  }

  /** True when this row is the signed-in administrator's own account. */
  protected isSelf(): boolean {
    return this.session.user()?.id === this.personId;
  }

  protected holds(roleId: string): boolean {
    return (this.person.data()?.roles ?? []).some((role) => role.id === roleId);
  }

  /**
   * Granting and revoking are separate calls rather than a diff, so a refusal names the role it
   * refused rather than failing a batch nobody can interpret.
   */
  protected toggleRole(roleId: string, held: boolean): void {
    this.formError.set(null);

    const request = held
      ? this.api.revokeRole(this.personId, roleId)
      : this.api.grantRole(this.personId, roleId);

    request.subscribe({
      next: (person) => {
        this.person.setSuccess(person);
        this.applyPlacement(person);
      },
      error: (error: AppError) => this.formError.set(error),
    });
  }

  /**
   * Choosing a team fills in the department and locks it; clearing the team frees it again.
   *
   * The department is never sent alongside a team - the server derives it, and a request naming a
   * different one is refused rather than corrected (spec FR-010, FR-011). The disabled control is a
   * courtesy that makes the rule visible; the server enforces it regardless.
   */
  protected onTeamChanged(teamId: string): void {
    if (!teamId) {
      this.placement.controls.departmentId.enable();
      return;
    }

    this.placement.controls.departmentId.disable();
  }

  protected onDepartmentChanged(departmentId: string): void {
    this.placement.controls.teamId.setValue('');
    this.teams.set([]);

    if (!departmentId) {
      return;
    }

    this.lookups.listTeams(departmentId).subscribe({ next: (page) => this.teams.set(page.items) });
  }

  protected savePlacement(): void {
    this.formError.set(null);

    const value = this.placement.getRawValue();
    const teamId = value.teamId || null;

    this.api
      .setPlacement(this.personId, {
        branchId: value.branchId || null,

        // Omitted when a team is chosen: the department follows the team, and sending both invites
        // the mismatch the server exists to refuse.
        departmentId: teamId ? null : value.departmentId || null,
        teamId,
      })
      .subscribe({
        next: (person) => {
          this.person.setSuccess(person);
          this.applyPlacement(person);
        },
        error: (error: AppError) => this.formError.set(error),
      });
  }

  protected toggleActivation(): void {
    this.formError.set(null);

    const current = this.person.data();

    if (!current) {
      return;
    }

    this.api.setActivation(this.personId, !current.summary.isActive).subscribe({
      next: (person) => this.person.setSuccess(person),
      error: (error: AppError) => this.formError.set(error),
    });
  }

  /**
   * Deleting asks first, in the page rather than in a browser dialog.
   *
   * Two reasons: a native confirm cannot say what is about to be revoked, and it cannot be read in
   * the reader's language. The confirmation states the consequences - roles revoked, sessions ended
   * now - because a person cannot weigh a decision whose effects are not in front of them.
   */
  protected remove(): void {
    this.formError.set(null);
    this.confirmingDelete.set(true);
  }

  protected cancelDelete(): void {
    this.confirmingDelete.set(false);
  }

  protected confirmDelete(): void {
    this.confirmingDelete.set(false);

    this.api.deletePerson(this.personId).subscribe({
      next: () => this.router.navigate(['/identity/people']),
      error: (error: AppError) => this.formError.set(error),
    });
  }

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

  private applyPlacement(person: PersonDetail): void {
    const current = person.summary.placement;

    this.placement.setValue({
      branchId: current.branchId ?? '',
      departmentId: current.departmentId ?? '',
      teamId: current.teamId ?? '',
    });

    if (current.teamId) {
      this.placement.controls.departmentId.disable();

      if (current.departmentId) {
        this.lookups
          .listTeams(current.departmentId)
          .subscribe({ next: (page) => this.teams.set(page.items) });
      }
    } else {
      this.placement.controls.departmentId.enable();
    }
  }
}
