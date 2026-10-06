import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { AvatarCache } from '../../../core/services/avatar-cache.service';

@Component({
  selector: 'vc-avatar',
  standalone: true,
  template: `
    <span class="vc-avatar-wrap" [style.width.px]="size()" [style.height.px]="size()">
      <span class="vc-avatar" role="img" [attr.aria-label]="ariaLabel()">
        @if (shownSrc()) {
          <img [src]="shownSrc()" alt="" />
        } @else {
          <span aria-hidden="true">{{ initials() }}</span>
        }
      </span>
      @if (statusEmoji()) {
        <span class="vc-avatar__status" aria-hidden="true">{{ statusEmoji() }}</span>
      }
    </span>
  `,
  styles: `
    :host {
      display: inline-grid;
      flex-shrink: 0;
    }
    .vc-avatar-wrap {
      position: relative;
      display: inline-grid;
    }
    .vc-avatar {
      display: inline-grid;
      place-items: center;
      width: 100%;
      height: 100%;
      border-radius: var(--vc-radius-md);
      background: linear-gradient(145deg, var(--vc-brand-soft), color-mix(in srgb, var(--vc-brand) 35%, var(--vc-surface-elevated)));
      color: var(--vc-brand-ink);
      font-family: var(--vc-font-display);
      font-weight: 600;
      font-size: 0.75rem;
      overflow: hidden;
    }
    img {
      width: 100%;
      height: 100%;
      object-fit: cover;
    }
    .vc-avatar__status {
      position: absolute;
      right: -0.2rem;
      bottom: -0.2rem;
      font-size: 0.7rem;
      line-height: 1;
    }
  `,
})
export class Avatar {
  private readonly cache = inject(AvatarCache);
  readonly name = input.required<string>();
  readonly src = input<string | undefined>(undefined);
  readonly authPath = input<string | null | undefined>(null);
  readonly size = input(32);
  readonly shownSrc = signal<string | undefined>(undefined);

  constructor() {
    effect(() => {
      const direct = this.src();
      const path = this.authPath();
      if (direct) {
        this.shownSrc.set(direct);
        return;
      }
      void this.cache.resolve(path).then((url) => this.shownSrc.set(url ?? undefined));
    });
  }
  readonly statusEmoji = input<string | null>(null);
  readonly statusLabel = input<string | null>(null);
  readonly ariaLabel = computed(() => {
    const label = this.statusLabel()?.trim();
    return label ? `${this.name()} — ${label}` : this.name();
  });
  readonly initials = computed(() => {
    const parts = this.name().trim().split(/\s+/).slice(0, 2);
    return parts.map((p) => p[0]?.toUpperCase() ?? '').join('') || '?';
  });
}
