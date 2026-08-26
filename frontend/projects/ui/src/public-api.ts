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

export { StateContainerComponent } from './lib/states/state-container.component';
export { LoadingStateComponent } from './lib/states/loading-state.component';
export { EmptyStateComponent } from './lib/states/empty-state.component';
export { ValidationErrorComponent } from './lib/states/validation-error.component';
export { ForbiddenStateComponent } from './lib/states/forbidden-state.component';
export { ServerErrorComponent } from './lib/states/server-error.component';
