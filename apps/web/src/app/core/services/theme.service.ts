import { Injectable, signal, effect, DestroyRef, inject } from '@angular/core';

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
    effect(() => {
      const theme = this.themeSignal();
      const root = document.documentElement;
      root.setAttribute('data-theme', theme);
      root.style.colorScheme = theme;
      if (this.userPinned) {
        localStorage.setItem(THEME_KEY, theme);
      }
    });
    effect(() => {
      const density = this.densitySignal();
      document.documentElement.setAttribute('data-density', density);
      localStorage.setItem(DENSITY_KEY, density);
    });

    // Follow OS theme only until the user pins a choice (B-049 polish).
    if (typeof window !== 'undefined' && !this.userPinned) {
      const media = window.matchMedia('(prefers-color-scheme: dark)');
      const onChange = (event: MediaQueryListEvent) => {
        if (!this.userPinned) {
          this.themeSignal.set(event.matches ? 'dark' : 'light');
        }
      };
      media.addEventListener('change', onChange);
      this.destroyRef.onDestroy(() => media.removeEventListener('change', onChange));
    }
  }

  toggleTheme(): void {
    this.userPinned = true;
    this.themeSignal.update((t) => (t === 'dark' ? 'light' : 'dark'));
  }

  setTheme(theme: ThemeMode): void {
    this.userPinned = true;
    this.themeSignal.set(theme);
  }

  setDensity(density: DensityMode): void {
    this.densitySignal.set(density);
  }

  toggleDensity(): void {
    this.densitySignal.update((d) => (d === 'compact' ? 'comfortable' : 'compact'));
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
