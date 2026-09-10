import { Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { ApiService } from '../../core/api/api.service';
import { AuthService } from '../../core/auth/auth.service';
import { ui } from '../../core/i18n/strings';

@Component({
  selector: 'vc-accept-invite-page',
  standalone: true,
  template: `
    <main class="vc-invite">
      <p role="status">{{ message() }}</p>
    </main>
  `,
  styles: `
    .vc-invite {
      min-height: 100dvh;
      display: grid;
      place-items: center;
      padding: var(--vc-space-6);
      color: var(--vc-ink);
      background: var(--vc-bg);
      font: inherit;
    }
  `,
})
export class AcceptInvitePage implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  readonly message = signal(ui.guestAccepting);

  async ngOnInit(): Promise<void> {
    if (!this.auth.ready()) {
      await this.auth.init();
    }
    if (!this.auth.isAuthenticated()) {
      const token = this.route.snapshot.paramMap.get('token') ?? '';
      if (typeof sessionStorage !== 'undefined') {
        sessionStorage.setItem('vc.returnUrl', `/invite/${token}`);
      }
      await this.router.navigateByUrl('/login');
      return;
    }

    const token = this.route.snapshot.paramMap.get('token') ?? '';
    try {
      await this.api.acceptChannelInvite(token);
      await this.router.navigateByUrl('/app');
    } catch {
      this.message.set(ui.guestExpired);
    }
  }
}
