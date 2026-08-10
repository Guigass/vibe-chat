import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
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
  constructor() {
    // Construct ThemeService so data-theme is applied synchronously on boot.
    inject(ThemeService);
  }
}
