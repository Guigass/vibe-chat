import { Injectable, signal, DestroyRef, inject } from '@angular/core';

export type ThemeMode = 'light' | 'dark';
export type DensityMode = 'comfortable' | 'compact';

const THEME_KEY = 'vc.theme';
const DENSITY_KEY = 'vc.density';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly destroyRef = inject(DestroyRef);
  private readonly themeSignal = signal<ThemeMode>(this.readTheme());
  private readonly densitySignal = signal<DensityMode>(this.readDensity());
  /** True after an explicit user toggle/set; system preference stops driving theme. */
  private userPinned = localStorage.getItem(THEME_KEY) === 'light' || localStorage.getItem(THEME_KEY) === 'dark';

  readonly theme = this.themeSignal.asReadonly();
  readonly density = this.densitySignal.asReadonly();

  constructor() {
    this.applyTheme(this.themeSignal());
    this.applyDensity(this.densitySignal());

    // Follow OS theme only until the user pins a choice (B-049 polish).
    if (typeof window !== 'undefined' && !this.userPinned) {
      const media = window.matchMedia('(prefers-color-scheme: dark)');
      const onChange = (event: MediaQueryListEvent) => {
        if (!this.userPinned) {
          const next: ThemeMode = event.matches ? 'dark' : 'light';
          this.themeSignal.set(next);
          this.applyTheme(next);
        }
      };
      media.addEventListener('change', onChange);
      this.destroyRef.onDestroy(() => media.removeEventListener('change', onChange));
    }
  }

  toggleTheme(): void {
    this.userPinned = true;
    const next: ThemeMode = this.themeSignal() === 'dark' ? 'light' : 'dark';
    this.themeSignal.set(next);
    this.applyTheme(next);
  }

  setTheme(theme: ThemeMode): void {
    this.userPinned = true;
    this.themeSignal.set(theme);
    this.applyTheme(theme);
  }

  setDensity(density: DensityMode): void {
    this.densitySignal.set(density);
    this.applyDensity(density);
  }

  toggleDensity(): void {
    const next: DensityMode = this.densitySignal() === 'compact' ? 'comfortable' : 'compact';
    this.densitySignal.set(next);
    this.applyDensity(next);
  }

  private applyTheme(theme: ThemeMode): void {
    const root = document.documentElement;
    root.setAttribute('data-theme', theme);
    root.style.colorScheme = theme;
    if (this.userPinned) {
      localStorage.setItem(THEME_KEY, theme);
    }
  }

  private applyDensity(density: DensityMode): void {
    document.documentElement.setAttribute('data-density', density);
    localStorage.setItem(DENSITY_KEY, density);
  }

  private readTheme(): ThemeMode {
    const stored = localStorage.getItem(THEME_KEY) as ThemeMode | null;
    if (stored === 'light' || stored === 'dark') return stored;
    return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
  }

  private readDensity(): DensityMode {
    const stored = localStorage.getItem(DENSITY_KEY) as DensityMode | null;
    return stored === 'compact' ? 'compact' : 'comfortable';
  }
}
