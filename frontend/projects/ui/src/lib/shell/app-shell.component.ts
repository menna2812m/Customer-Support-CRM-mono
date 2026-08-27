import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { MatToolbarModule } from '@angular/material/toolbar';
import { TranslocoPipe } from '@jsverse/transloco';
import { LanguageSwitcherComponent } from './language-switcher.component';

export interface ShellNavItem {
  /** Router path, relative to the application root. */
  path: string;
  /** Translation key for the visible label - never literal text (spec FR-035). */
  labelKey: string;
}

/**
 * Application shell: a toolbar, primary navigation, the language switcher, and the routed content
 * outlet.
 *
 * Audience-neutral by design (spec FR-033): navigation is supplied by the host application, so a
 * future external customer portal can reuse this shell with its own items instead of forking it.
 * Direction is applied globally from the active language, so nothing here is direction-aware.
 */
@Component({
  selector: 'crm-app-shell',
  imports: [
    LanguageSwitcherComponent,
    MatToolbarModule,
    RouterLink,
    RouterLinkActive,
    RouterOutlet,
    TranslocoPipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './app-shell.component.html',
  styleUrl: './app-shell.component.scss',
})
export class AppShellComponent {
  readonly navigation = input<ShellNavItem[]>([]);
}
