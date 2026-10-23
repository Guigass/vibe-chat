import { Component } from '@angular/core';

@Component({
  selector: 'vc-list',
  standalone: true,
  template: `<ul class="vc-list"><ng-content /></ul>`,
  styles: `:host { display: block; min-width: 0; }`,
})
export class UiList {}
