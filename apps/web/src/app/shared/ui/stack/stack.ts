import { Component, input } from '@angular/core';

@Component({
  selector: 'vc-stack',
  standalone: true,
  template: `<div class="vc-stack" [attr.data-gap]="gap()"><ng-content /></div>`,
  styles: `:host { display: block; min-width: 0; }`,
})
export class Stack {
  readonly gap = input<'1' | '2' | '3' | '4' | '6' | '8'>('4');
}
