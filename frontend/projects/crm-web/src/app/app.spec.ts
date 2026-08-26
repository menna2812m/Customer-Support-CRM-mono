import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideCrmTesting } from '@crm/core/testing';
import { App } from './app';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([]), provideCrmTesting()],
    }).compileComponents();
  });

  it('renders the shell with translated navigation', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Customer Support CRM');
    expect(text).toContain('Home');
    expect(text).toContain('Diagnostics');
  });
});
