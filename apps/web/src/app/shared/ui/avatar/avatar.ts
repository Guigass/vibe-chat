import { Component, computed, input } from '@angular/core';

@Component({
  selector: 'vc-avatar',
  standalone: true,
  template: `
    <span class="vc-avatar" [style.width.px]="size()" [style.height.px]="size()" [attr.aria-label]="name()">
      @if (src()) {
        <img [src]="src()" [alt]="name()" />
      } @else {
        <span aria-hidden="true">{{ initials() }}</span>
      }
    </span>
  `,
  styles: `
    .vc-avatar {
      display: inline-grid;
      place-items: center;
      border-radius: var(--vc-radius-md);
      background: linear-gradient(145deg, var(--vc-brand-soft), color-mix(in srgb, var(--vc-brand) 35%, var(--vc-surface-elevated)));
      color: var(--vc-brand-ink);
      font-family: var(--vc-font-display);
      font-weight: 600;
      font-size: 0.75rem;
      overflow: hidden;
      flex-shrink: 0;
    }
    img {
      width: 100%;
      height: 100%;
      object-fit: cover;
    }
  `,
})
export class Avatar {
  readonly name = input.required<string>();
  readonly src = input<string | undefined>(undefined);
  readonly size = input(32);
  readonly initials = computed(() => {
    const parts = this.name().trim().split(/\s+/).slice(0, 2);
    return parts.map((p) => p[0]?.toUpperCase() ?? '').join('') || '?';
  });
}
