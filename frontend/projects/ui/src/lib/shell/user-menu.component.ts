import { ChangeDetectionStrategy, Component, input, output, signal } from '@angular/core';
import { CdkMenu, CdkMenuItem, CdkMenuItemCheckbox, CdkMenuTrigger } from '@angular/cdk/menu';
import { MatButtonModule } from '@angular/material/button';
import { TranslocoPipe } from '@jsverse/transloco';

/** What the user asked for when they signed out. Both choices are theirs, and both are explicit. */
export interface SignOutChoice {
  allSessions: boolean;
  endProviderSession: boolean;
}

/**
 * The signed-in person and the ways out (contracts/frontend-contracts.md).
 *
 * Signing out of the CRM is unconditional. Signing out at the provider is a separate, opt-in choice
 * because it also ends every other corporate application open in this browser - a surprise nobody
 * wants from a CRM menu (spec FR-021).
 *
 * Built on the CDK menu rather than Angular Material's: this component sits in the shell, so it is
 * in the initial bundle for every visitor including one who is not signed in. The CDK gives the
 * behaviour that matters here - roving focus, Escape to close, `menuitemcheckbox` semantics for the
 * provider option - for a fraction of the download.
 *
 * Presentation only: the component reports the choice and the application performs it.
 */
@Component({
  selector: 'crm-user-menu',
  imports: [
    CdkMenu,
    CdkMenuItem,
    CdkMenuItemCheckbox,
    CdkMenuTrigger,
    MatButtonModule,
    TranslocoPipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <!-- The label names the person as well as the control. An aria-label replaces the visible text
         rather than adding to it, so one that said only "Signed in as" would take the name away
         from the very users who cannot see it. -->
    <button
      matButton
      type="button"
      [cdkMenuTriggerFor]="menu"
      [attr.aria-label]="'auth.userMenu' | transloco: { name: displayName() }"
      data-testid="user-menu"
    >
      {{ displayName() }}
    </button>

    <ng-template #menu>
      <div class="crm-user-menu" cdkMenu>
        <!-- Presentation, because a menu may contain only menu items: without this the heading
             would be an unfocusable child of role="menu" and some readers skip the lot. -->
        <p class="crm-user-menu__name" role="presentation">
          {{ 'auth.signedInAs' | transloco }} <strong>{{ displayName() }}</strong>
        </p>

        <!-- A checkbox item rather than a button: it announces its state, and choosing it does not
             sign anybody out on its own. -->
        <button
          class="crm-user-menu__item"
          type="button"
          cdkMenuItemCheckbox
          [cdkMenuItemChecked]="endProviderSession()"
          (cdkMenuItemTriggered)="endProviderSession.set(!endProviderSession())"
          data-testid="end-device-access"
        >
          {{ 'auth.endDeviceAccess' | transloco }}
        </button>

        <hr class="crm-user-menu__divider" />

        <button
          class="crm-user-menu__item"
          type="button"
          cdkMenuItem
          (cdkMenuItemTriggered)="choose(false)"
          data-testid="sign-out"
        >
          {{ 'auth.signOut' | transloco }}
        </button>

        <button
          class="crm-user-menu__item"
          type="button"
          cdkMenuItem
          (cdkMenuItemTriggered)="choose(true)"
          data-testid="sign-out-everywhere"
        >
          {{ 'auth.signOutEverywhere' | transloco }}
        </button>
      </div>
    </ng-template>
  `,
  styles: `
    .crm-user-menu {
      background: var(--mat-sys-surface-container);
      border-radius: var(--mat-sys-corner-extra-small);
      box-shadow: var(--mat-sys-level2);
      color: var(--mat-sys-on-surface);
      min-inline-size: 16rem;
      padding-block: var(--crm-space-xs);
    }

    .crm-user-menu__name {
      color: var(--mat-sys-on-surface-variant);
      font: var(--mat-sys-body-small);
      margin: 0;
      padding-block: var(--crm-space-xs);
      padding-inline: var(--crm-space-md);
    }

    .crm-user-menu__item {
      background: none;
      border: 0;
      color: inherit;
      cursor: pointer;
      display: block;
      font: var(--mat-sys-body-medium);
      inline-size: 100%;
      padding-block: var(--crm-space-sm);
      padding-inline: var(--crm-space-md);
      text-align: start;
    }

    .crm-user-menu__item:hover,
    .crm-user-menu__item:focus-visible {
      background: var(--mat-sys-surface-container-highest);
    }

    /* The tick is drawn from the ARIA state, so the visual and the announcement cannot disagree.
       The empty alternative text after the slash keeps a reader from announcing the glyph as well -
       aria-checked has already said "checked", and hearing it twice is noise. */
    .crm-user-menu__item[aria-checked='true']::before {
      content: '✓' / '';
      margin-inline-end: var(--crm-space-xs);
    }

    .crm-user-menu__item[aria-checked='false']::before {
      content: '';
      display: inline-block;
      inline-size: 1em;
      margin-inline-end: var(--crm-space-xs);
    }

    .crm-user-menu__divider {
      border: 0;
      border-block-start: 1px solid var(--mat-sys-outline-variant);
      margin-block: var(--crm-space-xs);
    }
  `,
})
export class UserMenuComponent {
  readonly displayName = input.required<string>();

  readonly signOut = output<SignOutChoice>();

  protected readonly endProviderSession = signal(false);

  protected choose(allSessions: boolean): void {
    this.signOut.emit({ allSessions, endProviderSession: this.endProviderSession() });
  }
}
