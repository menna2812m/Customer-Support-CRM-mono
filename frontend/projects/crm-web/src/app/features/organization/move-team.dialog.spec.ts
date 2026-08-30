import { TestBed } from '@angular/core/testing';
import { HttpTestingController } from '@angular/common/http/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideCrmTesting } from '@crm/core/testing';
import { MoveTeamDialog, MoveTeamDialogData } from './move-team.dialog';
import { OrganizationUnit, Team, TeamMoveResult } from './organization-api.service';

function unit(id: string, nameEn: string): OrganizationUnit {
  return { id, nameAr: nameEn, nameEn, code: id.toUpperCase(), isActive: true };
}

function team(id: string, departmentId: string): Team {
  return {
    ...unit(id, 'Tier 2'),
    departmentId,
    departmentNameAr: 'الدعم الفني',
    departmentNameEn: 'Technical Support',
  };
}

/**
 * Moving a team is the one action that writes outside the thing being edited: everyone on the team
 * has their recorded department changed with it. How many people that was is the part an
 * administrator cannot see for themselves, so losing it is losing the only evidence the move did
 * what it said (spec FR-015, AR-006).
 */
describe('MoveTeamDialog', () => {
  const closed: (TeamMoveResult | boolean | undefined)[] = [];

  const data: MoveTeamDialogData = {
    team: team('t1', 'd1'),
    departments: [unit('d2', 'Billing')],
  };

  beforeEach(() => {
    closed.length = 0;

    TestBed.configureTestingModule({
      providers: [
        provideCrmTesting(),
        { provide: MAT_DIALOG_DATA, useValue: data },
        {
          provide: MatDialogRef,
          useValue: {
            close: (result: TeamMoveResult | boolean | undefined) => closed.push(result),
          },
        },
      ],
    });
  });

  function confirmMove(): void {
    const fixture = TestBed.createComponent(MoveTeamDialog);
    fixture.detectChanges();

    const dialog = fixture.componentInstance as unknown as {
      form: { setValue: (value: { departmentId: string }) => void };
      confirm: () => void;
    };

    dialog.form.setValue({ departmentId: 'd2' });
    dialog.confirm();
  }

  it('hands back how many people the move reassigned', () => {
    confirmMove();

    const result: TeamMoveResult = { team: team('t1', 'd2'), membersReassigned: 3 };
    TestBed.inject(HttpTestingController)
      .expectOne('/api/v1/organization/teams/t1/department')
      .flush(result);

    expect(closed).toHaveLength(1);
    expect((closed[0] as TeamMoveResult).membersReassigned).toBe(3);
  });

  it('stays open showing why when the move is refused', () => {
    confirmMove();

    TestBed.inject(HttpTestingController)
      .expectOne('/api/v1/organization/teams/t1/department')
      .flush({ code: 'organization_department_inactive' }, { status: 409, statusText: 'Conflict' });

    // Closing here would look to the administrator exactly like a move that worked.
    expect(closed).toHaveLength(0);
  });
});
