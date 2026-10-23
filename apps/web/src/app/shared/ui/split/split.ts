import { Component } from '@angular/core';

@Component({
  selector: 'vc-split',
  standalone: true,
  template: `
    <div class="vc-split">
      <div class="vc-split__side"><ng-content select="[vcSplitSide]" /></div>
      <div class="vc-split__main"><ng-content /></div>
    </div>
  `,
  styles: `:host { display: block; min-width: 0; }`,
})
export class Split {}
