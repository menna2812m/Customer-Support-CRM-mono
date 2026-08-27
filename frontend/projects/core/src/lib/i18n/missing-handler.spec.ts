import { TranslocoConfig } from '@jsverse/transloco';
import { CrmMissingHandler } from './missing-handler';

/**
 * Spec FR-039: a missing key renders a documented fallback and is reported to developers - never
 * an empty label, which looks to a user like a broken screen and to a reviewer like nothing at all.
 */
describe('CrmMissingHandler', () => {
  const handler = new CrmMissingHandler();

  function config(prodMode: boolean): TranslocoConfig {
    return { prodMode } as TranslocoConfig;
  }

  it('renders the key itself as the documented fallback', () => {
    expect(handler.handle('diagnostics.missingLabel', config(false))).toBe(
      'diagnostics.missingLabel',
    );
  });

  it('reports the gap to developers outside production', () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);

    handler.handle('nav.missing', config(false));

    expect(warn).toHaveBeenCalledOnce();
    expect(warn.mock.calls[0][0]).toContain('nav.missing');

    warn.mockRestore();
  });

  it('stays quiet in production, where a console warning helps nobody', () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);

    const result = handler.handle('nav.missing', config(true));

    expect(warn).not.toHaveBeenCalled();
    expect(result).toBe('nav.missing');

    warn.mockRestore();
  });
});
