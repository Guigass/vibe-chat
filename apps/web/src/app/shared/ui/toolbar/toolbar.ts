import { Component, input } from '@angular/core';

@Component({
  selector: 'vc-toolbar',
  standalone: true,
  template: `
    <div class="vc-toolbar" role="toolbar" [attr.aria-label]="label() || null">
      <div class="vc-toolbar__start"><ng-content select="[vcToolbarStart]" /></div>
      <div class="vc-toolbar__end"><ng-content /></div>
    </div>
  `,
  styles: `:host { display: block; min-width: 0; }`,
})
export class Toolbar {
  readonly label = input('');
}
