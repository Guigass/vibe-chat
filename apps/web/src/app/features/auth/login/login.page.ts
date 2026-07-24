import { Component, inject, OnInit } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '../../../core/auth/auth.service';
import { Button, ThemeToggle } from '../../../shared/ui';

@Component({
  selector: 'vc-login-page',
  standalone: true,
  imports: [Button, ThemeToggle],
  templateUrl: './login.page.html',
  styleUrl: './login.page.scss',
})
export class LoginPage implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly error = this.auth.error;
  loading = false;

  async ngOnInit(): Promise<void> {
    if (!this.auth.ready()) {
      await this.auth.init();
    }
    if (this.auth.isAuthenticated()) {
      await this.router.navigateByUrl('/app');
    }
  }

  async login(): Promise<void> {
    this.loading = true;
    try {
      await this.auth.login();
    } catch (err) {
      this.loading = false;
      console.error(err);
    }
  }

  async enterDemo(): Promise<void> {
    this.auth.enterDemoMode();
    await this.router.navigateByUrl('/app');
  }
}
