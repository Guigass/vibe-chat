import { Component, OnInit, inject } from '@angular/core';
import { AuthService } from '../../../core/auth/auth.service';
import { ui } from '../../../core/i18n/strings';

@Component({
  selector: 'vc-silent-renew-page',
  standalone: true,
  template: `<p class="vc-sr-only">{{ ui.authRenewing }}</p>`,
})
export class SilentRenewPage implements OnInit {
  readonly ui = ui;
  private readonly auth = inject(AuthService);

  async ngOnInit(): Promise<void> {
    await this.auth.completeSilentRenew();
  }
}
