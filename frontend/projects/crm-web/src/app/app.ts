import { ChangeDetectionStrategy, Component, DOCUMENT, computed, inject } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '@crm/core';
import { AppShellComponent, ShellNavItem, SignOutChoice, UserMenuComponent } from '@crm/ui';

@Component({
  imports: [AppShellComponent, UserMenuComponent],
  selector: 'crm-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './app.scss',
  templateUrl: './app.html',
})
export class App {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly document = inject(DOCUMENT);

  /**
   * Navigation is supplied by the application, keeping the shell audience-neutral (FR-033).
   *
   * An item with no permission is reachable by anyone signed in; the rest are shown only to a
   * session that carries the named permission.
   */
  private readonly allNavigation: readonly PermittedNavItem[] = [
    { path: '/home', labelKey: 'nav.home' },
    { path: '/diagnostics', labelKey: 'nav.diagnostics', permission: 'diagnostics.read' },
    // Two entries rather than one. A branch belongs to nothing and contains nothing (FR-003), so
    // unlike a team there is no parent screen to reach it through - it needs its own destination or
    // it has none at all.
    {
      path: '/identity/people',
      labelKey: 'nav.people',
      permission: 'identity.view',
    },
    {
      path: '/organization/departments',
      labelKey: 'nav.departments',
      permission: 'organization.view',
    },
    {
      path: '/organization/branches',
      labelKey: 'nav.branches',
      permission: 'organization.view',
    },
  ];

  /**
   * What this session may see. Presentation only, and never a security boundary (Constitution IV):
   * hiding a link removes a dead end from the toolbar, and the API refuses the route's data
   * regardless of whether the link was ever rendered. Typing the address directly reaches the same
   * refusal.
   *
   * Recomputed from the permission signal, so a role change that lands on the next renewal changes
   * the toolbar without a reload.
   */
  protected readonly navigation = computed<ShellNavItem[]>(() => {
    // Read so the computation re-runs when the session changes; `hasPermission` is a plain call.
    this.auth.permissions();

    return this.allNavigation
      .filter((item) => !item.permission || this.auth.hasPermission(item.permission))
      .map(({ path, labelKey }) => ({ path, labelKey }));
  });

  protected readonly user = this.auth.user;

  /**
   * Ends the CRM session first, then leaves for the provider only if the user asked. The order
   * matters: were the provider visited first, the browser would depart before this application had
   * ended its own session, and the CRM session would outlive the sign-out.
   */
  protected async signOut(choice: SignOutChoice): Promise<void> {
    const providerSignOutUrl = await this.auth.signOut(choice);

    if (providerSignOutUrl) {
      this.document.location.assign(providerSignOutUrl);

      return;
    }

    await this.router.navigate(['/sign-in'], { replaceUrl: true });
  }
}

/** A navigation item plus the permission, if any, that a session needs before it is offered. */
interface PermittedNavItem extends ShellNavItem {
  permission?: string;
}
