import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { AppError, RequestState } from '@crm/core';
import { provideCrmTesting } from '@crm/core/testing';
import { StateContainerComponent } from './state-container.component';

/**
 * Constitution X / spec FR-032: every one of the six states renders something meaningful, and
 * content is projected only on success. This is the component every future screen leans on, so a
 * regression here would be inherited by the whole CRM.
 */
@Component({
  imports: [StateContainerComponent],
  template: `
    <crm-state-container [state]="state()">
      <p>projected content</p>
    </crm-state-container>
  `,
})
class HostComponent {
  readonly state = signal<RequestState<string[]>>({ status: 'idle' });
}

describe('StateContainerComponent', () => {
  async function render(state: RequestState<string[]>): Promise<string> {
    await TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [provideCrmTesting()],
    }).compileComponents();

    const fixture = TestBed.createComponent(HostComponent);
    fixture.componentInstance.state.set(state);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  it('shows a loading indicator while loading, and no content', async () => {
    const text = await render({ status: 'loading' });

    expect(text).toContain('Loading');
    expect(text).not.toContain('projected content');
  });

  it('shows the empty state, not an error, when there is nothing to display', async () => {
    const text = await render({ status: 'empty' });

    expect(text).toContain('Nothing here yet');
    expect(text).not.toContain('projected content');
  });

  it('projects the content only on success', async () => {
    const text = await render({ status: 'success', data: ['a'] });

    expect(text).toContain('projected content');
  });

  it('shows field-level messages for a validation failure', async () => {
    const error: AppError = {
      kind: 'validation',
      code: 'validation_failed',
      correlationId: 'c1',
      fieldErrors: {
        message: [{ code: 'required', message: 'server text that must not be shown' }],
      },
    };

    const text = await render({ status: 'error', error });

    // LR-003: the per-field code is translated; server-supplied text is never rendered.
    expect(text).toContain('This value is required.');
    expect(text).not.toContain('server text that must not be shown');
    expect(text).not.toContain('projected content');
  });

  it('shows an access message for a forbidden failure', async () => {
    const error: AppError = { kind: 'forbidden', code: 'forbidden', correlationId: 'c2' };

    expect(await render({ status: 'error', error })).toContain('do not have access');
  });

  it('shows the correlation id for a server failure so support can trace it', async () => {
    const error: AppError = { kind: 'server', code: 'unexpected_error', correlationId: 'trace-42' };

    const text = await render({ status: 'error', error });

    expect(text).toContain('Something went wrong');
    expect(text).toContain('trace-42');
  });
});
