import { Component, inject } from '@angular/core';
import { ThemeService } from '../../../core/services/theme.service';
import { IconButton } from '../icon-button/icon-button';
import { ui } from '../../../core/i18n/strings';

@Component({
  selector: 'vc-theme-toggle',
  standalone: true,
  imports: [IconButton],
  template: `
    <vc-icon-button
      [label]="theme.theme() === 'dark' ? ui.themeLight : ui.themeDark"
      (click)="theme.toggleTheme()"
    >
      @if (theme.theme() === 'dark') {
        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8">
          <circle cx="12" cy="12" r="4" />
          <path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4" />
        </svg>
      } @else {
        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8">
          <path d="M21 14.5A8.5 8.5 0 0 1 9.5 3 7 7 0 1 0 21 14.5z" />
        </svg>
      }
    </vc-icon-button>
  `,
})
export class ThemeToggle {
  readonly theme = inject(ThemeService);
  readonly ui = ui;
}
