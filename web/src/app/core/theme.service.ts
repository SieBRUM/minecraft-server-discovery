import { DOCUMENT } from '@angular/common';
import { Injectable, computed, effect, inject, signal } from '@angular/core';

export type ThemeMode = 'system' | 'light' | 'dark';

const STORAGE_KEY = 'mcd-theme';

/**
 * Light/dark mode. "system" follows the OS setting. The choice is a per-browser convenience kept in
 * localStorage (wrapped in try/catch: storage can be unavailable in private windows).
 */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly document = inject(DOCUMENT);
  private readonly media =
    this.document.defaultView?.matchMedia?.('(prefers-color-scheme: dark)') ?? null;
  private readonly systemDark = signal(this.media?.matches ?? false);

  readonly mode = signal<ThemeMode>(this.load());
  readonly dark = computed(() =>
    this.mode() === 'system' ? this.systemDark() : this.mode() === 'dark',
  );

  constructor() {
    this.media?.addEventListener?.('change', (e) => this.systemDark.set(e.matches));
    effect(() => {
      const root = this.document.documentElement;
      // Material's light-dark() tokens follow color-scheme; data-theme lets our own CSS do the same.
      root.style.colorScheme = this.dark() ? 'dark' : 'light';
      root.dataset['theme'] = this.dark() ? 'dark' : 'light';
    });
  }

  /** system → light → dark → system */
  cycle(): void {
    const next: ThemeMode =
      this.mode() === 'system' ? 'light' : this.mode() === 'light' ? 'dark' : 'system';
    this.mode.set(next);
    try {
      localStorage.setItem(STORAGE_KEY, next);
    } catch {
      // storage unavailable: the choice just isn't remembered
    }
  }

  private load(): ThemeMode {
    try {
      const value = localStorage.getItem(STORAGE_KEY);
      return value === 'light' || value === 'dark' ? value : 'system';
    } catch {
      return 'system';
    }
  }
}

/**
 * Resolves a CSS custom property to a concrete colour for canvas drawing (charts, map). Our tokens use
 * light-dark(), which canvas APIs can't parse, so let the browser compute it on a probe element.
 */
export function cssVar(name: string, fallback: string): string {
  const probe = document.createElement('span');
  probe.style.display = 'none';
  probe.style.color = `var(${name}, ${fallback})`;
  document.body.appendChild(probe);
  const value = getComputedStyle(probe).color;
  probe.remove();
  return value || fallback;
}
