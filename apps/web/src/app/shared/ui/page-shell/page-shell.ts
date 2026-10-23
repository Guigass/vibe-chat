import { Component } from '@angular/core';

@Component({
  selector: 'vc-page',
  standalone: true,
  template: `<div class="vc-page"><ng-content /></div>`,
  styles: `:host { display: block; min-width: 0; }`,
})
export class PageShell {}
