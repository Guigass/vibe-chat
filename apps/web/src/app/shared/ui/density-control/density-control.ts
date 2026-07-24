import { Component, inject } from '@angular/core';
import { ThemeService } from '../../../core/services/theme.service';
import { IconButton } from '../icon-button/icon-button';

@Component({
  selector: 'vc-density-control',
  standalone: true,
  imports: [IconButton],
  template: `
    <vc-icon-button
      [label]="theme.density() === 'compact' ? 'Densidade confortável' : 'Densidade compacta'"
      (click)="theme.toggleDensity()"
    >
      <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8">
        @if (theme.density() === 'compact') {
          <path d="M4 7h16M4 12h16M4 17h16" />
        } @else {
          <path d="M4 6h16M4 12h16M4 18h16" />
        }
      </svg>
    </vc-icon-button>
  `,
})
export class DensityControl {
  readonly theme = inject(ThemeService);
}
