import { Component, OnInit, inject } from '@angular/core';
import { AuthService } from '../../../core/auth/auth.service';

@Component({
  selector: 'vc-silent-renew-page',
  standalone: true,
  template: `<p class="vc-sr-only">Renovando sessão…</p>`,
})
export class SilentRenewPage implements OnInit {
  private readonly auth = inject(AuthService);

  async ngOnInit(): Promise<void> {
    await this.auth.completeSilentRenew();
  }
}
