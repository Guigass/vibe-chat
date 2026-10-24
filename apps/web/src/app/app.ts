import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { AppUpdateService } from './core/services/app-update.service';
import { BrandingService } from './core/services/branding.service';
import { ThemeService } from './core/services/theme.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  template: `<router-outlet />`,
  styles: `
    :host {
      display: block;
      height: 100%;
      min-height: 100%;
    }
  `,
})
export class App {
  /** Field inject applies data-theme synchronously on bootstrap. */
  private readonly theme = inject(ThemeService);
  private readonly updates = inject(AppUpdateService);
  private readonly branding = inject(BrandingService);

  constructor() {
    this.updates.start();
  }
}
