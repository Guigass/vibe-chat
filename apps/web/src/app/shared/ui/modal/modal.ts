import { A11yModule } from '@angular/cdk/a11y';
import { Component, input, model } from '@angular/core';

@Component({
  selector: 'vc-modal',
  standalone: true,
  imports: [A11yModule],
  template: `
    @if (open()) {
      <div class="vc-modal__backdrop" (click)="close()">
        <div
          class="vc-modal"
          role="dialog"
          aria-modal="true"
          cdkTrapFocus
          [cdkTrapFocusAutoCapture]="true"
          [attr.aria-labelledby]="titleId()"
          (click)="$event.stopPropagation()"
          (keydown.escape)="close()"
        >
          <header class="vc-modal__head">
            <h2 [id]="titleId()">{{ title() }}</h2>
            <button type="button" class="vc-modal__close" (click)="close()">{{ closeLabel() }}</button>
          </header>
          <div class="vc-modal__body"><ng-content /></div>
        </div>
      </div>
    }
  `,
  styles: `:host { display: contents; }`,
})
export class Modal {
  readonly open = model(false);
  readonly title = input('');
  readonly closeLabel = input('');
  readonly titleId = input('vc-modal-title');

  close(): void {
    this.open.set(false);
  }
}
