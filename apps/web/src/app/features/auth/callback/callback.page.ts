import { Component, OnInit, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '../../../core/auth/auth.service';
import { ui } from '../../../core/i18n/strings';

@Component({
  selector: 'vc-callback-page',
  standalone: true,
  template: `
    <section class="callback">
      <p class="brand">VibeChat</p>
      @if (error()) {
        <p role="alert">{{ error() }}</p>
      } @else {
        <p>{{ ui.authCompleting }}</p>
      }
    </section>
  `,
  styles: `
    .callback {
      min-height: 100dvh;
      display: grid;
      place-content: center;
      gap: 0.5rem;
      text-align: center;
      background: var(--vc-bg-atmosphere);
    }
    .brand {
      margin: 0;
      font-family: var(--vc-font-display);
      font-size: 1.8rem;
      color: var(--vc-brand);
    }
  `,
})
export class CallbackPage implements OnInit {
  readonly ui = ui;
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  readonly error = signal<string | null>(null);

  async ngOnInit(): Promise<void> {
    try {
      await this.auth.completeLogin();
      const next =
        typeof sessionStorage !== 'undefined' ? sessionStorage.getItem('vc.returnUrl') : null;
      if (next) {
        sessionStorage.removeItem('vc.returnUrl');
      }
      await this.router.navigateByUrl(next && next.startsWith('/') ? next : '/app');
    } catch (err) {
      this.error.set(err instanceof Error ? err.message : ui.authCallbackFailed);
    }
  }
}
