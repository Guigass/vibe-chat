import { Component, input } from '@angular/core';

@Component({
  selector: 'vc-skeleton',
  standalone: true,
  template: `<span class="vc-skeleton" [style.width]="width()" [style.height]="height()" aria-hidden="true"></span>`,
  styles: `
    .vc-skeleton {
      display: inline-block;
      border-radius: var(--vc-radius-sm);
      background: linear-gradient(
        90deg,
        color-mix(in srgb, var(--vc-ink) 8%, transparent),
        color-mix(in srgb, var(--vc-ink) 14%, transparent),
        color-mix(in srgb, var(--vc-ink) 8%, transparent)
      );
      background-size: 200% 100%;
      animation: vc-shimmer 1.2s ease-in-out infinite;
    }
    @keyframes vc-shimmer {
      0% {
        background-position: 100% 0;
      }
      100% {
        background-position: -100% 0;
      }
    }
  `,
})
export class Skeleton {
  readonly width = input('100%');
  readonly height = input('1rem');
}
