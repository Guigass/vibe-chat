import { Component, input } from '@angular/core';

@Component({
  selector: 'vc-table',
  standalone: true,
  template: `
    <div class="vc-table-wrap">
      <table class="vc-table">
        @if (caption()) {
          <caption>{{ caption() }}</caption>
        }
        <ng-content />
      </table>
    </div>
  `,
  styles: `:host { display: block; min-width: 0; }`,
})
export class DataTable {
  readonly caption = input('');
}
