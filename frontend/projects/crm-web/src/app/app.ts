import { ChangeDetectionStrategy, Component } from '@angular/core';
import { AppShellComponent, ShellNavItem } from '@crm/ui';

@Component({
  imports: [AppShellComponent],
  selector: 'crm-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './app.scss',
  templateUrl: './app.html',
})
export class App {
  /** Navigation is supplied by the application, keeping the shell audience-neutral (FR-033). */
  protected readonly navigation: ShellNavItem[] = [
    { path: '/home', labelKey: 'nav.home' },
    { path: '/diagnostics', labelKey: 'nav.diagnostics' },
  ];
}
