import { Component, input } from '@angular/core';

@Component({
  selector: 'vc-card',
  standalone: true,
  template: `
    <section class="vc-card">
      @if (title()) {
        <h2 class="vc-card__title">{{ title() }}</h2>
      }
      <div class="vc-card__body"><ng-content /></div>
    </section>
  `,
  styles: `:host { display: block; min-width: 0; }`,
})
export class Card {
  readonly title = input('');
}
