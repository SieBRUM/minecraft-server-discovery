import { TestBed } from '@angular/core/testing';
import { ThemeService } from './theme.service';

describe('ThemeService', () => {
  beforeEach(() => localStorage.clear());

  it('cycles system → light → dark and applies the colour scheme', async () => {
    const theme = TestBed.inject(ThemeService);
    expect(theme.mode()).toBe('system');
    theme.cycle();
    TestBed.tick();
    expect(theme.mode()).toBe('light');
    expect(document.documentElement.style.colorScheme).toBe('light');
    theme.cycle();
    TestBed.tick();
    expect(theme.dark()).toBe(true);
    expect(document.documentElement.dataset['theme']).toBe('dark');
    expect(localStorage.getItem('mcd-theme')).toBe('dark');
  });
});
