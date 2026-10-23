import { Component, input } from '@angular/core';

@Component({
  selector: 'vc-cluster',
  standalone: true,
  template: `
    <div
      class="vc-cluster"
      [class.vc-cluster--end]="justify() === 'end'"
      [class.vc-cluster--between]="justify() === 'between'"
      [attr.data-gap]="gap()"
    >
      <ng-content />
    </div>
  `,
  styles: `:host { display: block; min-width: 0; }`,
})
export class Cluster {
  readonly gap = input<'1' | '2' | '3' | '4'>('2');
  readonly justify = input<'start' | 'end' | 'between'>('start');
}
