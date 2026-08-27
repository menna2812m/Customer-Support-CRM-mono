import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { HttpTestingController } from '@angular/common/http/testing';
import { provideCrmTesting } from '@crm/core/testing';
import { AuthCompletePage } from './auth-complete.page';
import { NoAccessPage } from './no-access.page';
import { SignInPage } from './sign-in.page';

/** A route whose query parameters the screen reads on creation, which is what these screens do. */
function routeWith(queryParams: Record<string, string>) {
  return {
    provide: ActivatedRoute,
    useValue: { snapshot: { queryParamMap: convertToParamMap(queryParams) } },
  };
}

function configure(queryParams: Record<string, string> = {}) {
  TestBed.configureTestingModule({
    providers: [provideCrmTesting(), provideRouter([]), routeWith(queryParams)],
  });
}

function textOf(fixture: { nativeElement: unknown }): string {
  return (fixture.nativeElement as HTMLElement).textContent ?? '';
}

describe('SignInPage', () => {
  it('explains the departure and offers no credential fields of its own', async () => {
    configure();

    const fixture = TestBed.createComponent(SignInPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;

    expect(textOf(fixture)).toContain('You will be taken to your organisation sign-in page');

    // The provider owns credential entry. A password field here would mean this application had
    // started collecting credentials, which is the one thing the design forbids.
    expect(element.querySelectorAll('input[type="password"]')).toHaveLength(0);
    expect(element.querySelector('[data-testid="sign-in"]')).not.toBeNull();
  });

  it('explains a provider outage instead of blaming the user', async () => {
    configure({ error: 'provider_unavailable' });

    const fixture = TestBed.createComponent(SignInPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const alert = (fixture.nativeElement as HTMLElement).querySelector('[role="alert"]');

    expect(alert?.textContent).toContain('The sign-in service is temporarily unavailable');
    expect(alert?.textContent ?? '').not.toContain('credential');
  });
});

describe('NoAccessPage', () => {
  it('renders its own message with the correlation identifier support will ask for', async () => {
    configure({ correlationId: 'trace-99' });

    const fixture = TestBed.createComponent(NoAccessPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;

    expect(textOf(fixture)).toContain('no permissions have been granted');
    expect(element.querySelector('[data-testid="correlation-id"]')?.textContent).toBe('trace-99');
  });

  it('explains a collision differently, because it needs an administrator rather than a retry', async () => {
    configure({ error: 'identity_collision', correlationId: 'trace-100' });

    const fixture = TestBed.createComponent(NoAccessPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(textOf(fixture)).toContain('administrator to resolve a conflict');
  });
});

describe('AuthCompletePage', () => {
  it('exchanges the cookie and continues to the destination the visitor asked for', async () => {
    configure({ returnUrl: '/diagnostics' });

    const router = TestBed.inject(Router);
    const navigated: string[] = [];
    vi.spyOn(router, 'navigateByUrl').mockImplementation((url) => {
      navigated.push(String(url));
      return Promise.resolve(true);
    });

    const fixture = TestBed.createComponent(AuthCompletePage);

    // Awaited directly rather than through change detection: the screen's whole behaviour is this
    // one asynchronous exchange, and the test should wait for it rather than for a render.
    const completed = fixture.componentInstance.ngOnInit();

    TestBed.inject(HttpTestingController)
      .expectOne('/api/v1/auth/session')
      .flush({
        accessToken: 'issued-credential',
        expiresInSeconds: 900,
        user: {
          id: 'a1',
          displayName: 'Layla Hassan',
          email: 'layla@example.com',
          population: 'Staff',
          permissions: ['tickets.view'],
          scope: null,
        },
      });

    await completed;

    expect(navigated).toEqual(['/diagnostics']);
  });

  it('sends a refused sign-in to the screen that can explain it, without calling the API', async () => {
    configure({ error: 'no_access', correlationId: 'trace-7' });

    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    const fixture = TestBed.createComponent(AuthCompletePage);
    await fixture.componentInstance.ngOnInit();

    expect(navigate).toHaveBeenCalledWith(
      ['/no-access'],
      expect.objectContaining({
        queryParams: { error: 'no_access', correlationId: 'trace-7' },
        replaceUrl: true,
      }),
    );

    // A refusal is already decided; asking the API again would only produce a second refusal.
    TestBed.inject(HttpTestingController).verify();
  });
});
