/*
 * Public API surface of @crm/ui.
 *
 * Reusable presentation: the application shell, the six mandated screen-state components, and the
 * single theme definition (theme/_theme.scss, imported by each application's styles).
 * No feature logic lives here.
 */

export { AppShellComponent } from './lib/shell/app-shell.component';
export type { ShellNavItem } from './lib/shell/app-shell.component';

export { LanguageSwitcherComponent } from './lib/shell/language-switcher.component';

export { UserMenuComponent } from './lib/shell/user-menu.component';
export type { SignOutChoice } from './lib/shell/user-menu.component';

export { StateContainerComponent } from './lib/states/state-container.component';
export { LoadingStateComponent } from './lib/states/loading-state.component';
export { EmptyStateComponent } from './lib/states/empty-state.component';
export { ValidationErrorComponent } from './lib/states/validation-error.component';
export { ForbiddenStateComponent } from './lib/states/forbidden-state.component';
export { ServerErrorComponent } from './lib/states/server-error.component';

/*
 * Design-system primitives (003 design refactor).
 *
 * These live here rather than in the application because @crm/ui is what a second application - the
 * planned external customer portal - imports to inherit the same look. A primitive in
 * crm-web/src/app/shared would be unreachable from there, and the design system would split in two.
 */
export { PageHeaderComponent } from './lib/layout/page-header.component';
export { PanelComponent } from './lib/layout/panel.component';
export { BadgeComponent } from './lib/data/badge.component';
export type { BadgeTone } from './lib/data/badge.component';
export { CodeComponent } from './lib/data/code.component';

export { UnitNamePipe } from './lib/data/unit-name.pipe';
export type { BilingualName } from './lib/data/unit-name.pipe';
export { NoticeComponent } from './lib/states/notice.component';
