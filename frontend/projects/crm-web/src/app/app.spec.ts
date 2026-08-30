import { TestBed } from '@angular/core/testing';
import { AuthSession, AuthUser } from '@crm/core';
import { provideCrmTesting } from '@crm/core/testing';
import { App } from './app';

/**
 * Spec FR-033 and Constitution IV: the toolbar shows what this session can actually use, and
 * hiding a link is a courtesy rather than a control.
 */
describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideCrmTesting()],
    }).compileComponents();
  });

  async function render(): Promise<string> {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  function signIn(...permissions: string[]): void {
    const user: AuthUser = {
      id: 'a1',
      displayName: 'Layla Hassan',
      email: 'layla@example.com',
      population: 'Staff',
      permissions,
      scope: null,
    };

    TestBed.inject(AuthSession).set('issued-credential', user);
  }

  it('renders the shell with translated navigation', async () => {
    signIn('diagnostics.read');

    const text = await render();

    expect(text).toContain('Customer Support CRM');
    expect(text).toContain('Home');
    expect(text).toContain('Diagnostics');
  });

  // Both organization screens are top-level destinations. Branches belong to nothing and contain
  // nothing (FR-003), so there is no department to reach them through - without their own entry
  // the screen exists and is routed and no one can get to it.
  it('offers both organization screens to a session that may see them', async () => {
    signIn('organization.view');

    const text = await render();

    expect(text).toContain('Departments');
    expect(text).toContain('Branches');
  });

  it('offers only the destinations this session may reach', async () => {
    signIn('tickets.view');

    const text = await render();

    // Home asks for no permission, so everybody signed in keeps it.
    expect(text).toContain('Home');

    // A link to a screen the API would refuse is a dead end, so it is not offered.
    expect(text).not.toContain('Diagnostics');
  });

  it('offers nothing permissioned before anybody has signed in', async () => {
    const text = await render();

    expect(text).not.toContain('Diagnostics');
  });

  it('hides a link without ever standing in for the backend refusing', async () => {
    signIn('tickets.view');

    await render();

    // The assertion behind the whole design: the route is still there and still reachable by
    // typing the address. What stops the caller is the API, which never sees the toolbar
    // (Constitution IV). If this were a security boundary, removing the link would be enough -
    // and it is deliberately not.
    const { routes } = await import('./app.routes');

    expect(routes.some((route) => route.path === 'diagnostics')).toBe(true);
  });
});
