import { Component, inject } from '@angular/core';
import { AppUpdateService } from '../../../core/services/app-update.service';
import { Button } from '../button/button';

@Component({
  selector: 'vc-update-banner',
  standalone: true,
  imports: [Button],
  template: `
    @if (updates.updateAvailable()) {
      <div class="vc-update" role="status">
        <span class="vc-update__text">Nova versão disponível</span>
        <vc-button type="button" variant="primary" (click)="onUpdate()">Atualizar</vc-button>
      </div>
    }
  `,
  styles: `
    .vc-update {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 0.75rem;
      padding: 0.45rem 0.75rem;
      background: color-mix(in srgb, var(--vc-brand) 14%, var(--vc-surface-elevated));
      color: var(--vc-ink);
      font-size: 0.85rem;
      font-weight: 500;
      border-bottom: 1px solid var(--vc-border);
      animation: vc-update-in 220ms var(--vc-ease-out);
    }

    .vc-update__text {
      letter-spacing: -0.01em;
    }

    .vc-update vc-button {
      flex: 0 0 auto;
    }

    .vc-update ::ng-deep .vc-btn {
      min-height: 2rem;
      padding: 0.35rem 0.85rem;
      font-size: 0.82rem;
    }

    @keyframes vc-update-in {
      from {
        opacity: 0;
        transform: translateY(-0.25rem);
      }
      to {
        opacity: 1;
        transform: translateY(0);
      }
    }

    @media (prefers-reduced-motion: reduce) {
      .vc-update {
        animation: none;
      }
    }
  `,
})
export class UpdateBanner {
  readonly updates = inject(AppUpdateService);

  onUpdate(): void {
    void this.updates.applyUpdate();
  }
}
