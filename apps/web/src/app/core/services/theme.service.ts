import { Injectable, signal, effect } from '@angular/core';

export type ThemeMode = 'light' | 'dark';
export type DensityMode = 'comfortable' | 'compact';

const THEME_KEY = 'vc.theme';
const DENSITY_KEY = 'vc.density';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly themeSignal = signal<ThemeMode>(this.readTheme());
  private readonly densitySignal = signal<DensityMode>(this.readDensity());

  readonly theme = this.themeSignal.asReadonly();
  readonly density = this.densitySignal.asReadonly();

  constructor() {
    effect(() => {
      const theme = this.themeSignal();
      document.documentElement.setAttribute('data-theme', theme);
      localStorage.setItem(THEME_KEY, theme);
    });
    effect(() => {
      const density = this.densitySignal();
      document.documentElement.setAttribute('data-density', density);
      localStorage.setItem(DENSITY_KEY, density);
    });
  }

  toggleTheme(): void {
    this.themeSignal.update((t) => (t === 'dark' ? 'light' : 'dark'));
  }

  setTheme(theme: ThemeMode): void {
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
