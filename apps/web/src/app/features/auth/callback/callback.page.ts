import { Component, OnInit, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '../../../core/auth/auth.service';

@Component({
  selector: 'vc-callback-page',
  standalone: true,
  template: `
    <section class="callback">
      <p class="brand">VibeChat</p>
      @if (error()) {
        <p role="alert">{{ error() }}</p>
      } @else {
        <p>Concluindo autenticação…</p>
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
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  readonly error = signal<string | null>(null);

  async ngOnInit(): Promise<void> {
    try {
      await this.auth.completeLogin();
      await this.router.navigateByUrl('/app');
    } catch (err) {
      this.error.set(err instanceof Error ? err.message : 'Falha no callback OIDC');
    }
  }
}
