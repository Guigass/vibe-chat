import { Component, inject, OnInit } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ThemeService } from './core/services/theme.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  template: `<router-outlet />`,
  styles: `
    :host {
      display: block;
      min-height: 100%;
    }
  `,
})
export class App implements OnInit {
  private readonly theme = inject(ThemeService);

  ngOnInit(): void {
    // Ensure theme attributes are applied on boot.
    void this.theme.theme();
  }
}
