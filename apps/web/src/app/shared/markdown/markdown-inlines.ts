import { Component, input } from '@angular/core';
import { MarkdownInline } from './restricted-markdown';

@Component({
  selector: 'vc-markdown-inlines',
  standalone: true,
  imports: [MarkdownInlines],
  template: `
    @for (inline of inlines(); track $index) {
      @switch (inline.kind) {
        @case ('text') {
          {{ inline.text }}
        }
        @case ('strong') {
          <strong><vc-markdown-inlines [inlines]="inline.children" /></strong>
        }
        @case ('em') {
          <em><vc-markdown-inlines [inlines]="inline.children" /></em>
        }
        @case ('del') {
          <del><vc-markdown-inlines [inlines]="inline.children" /></del>
        }
        @case ('code') {
          <code>{{ inline.text }}</code>
        }
        @case ('link') {
          <a [href]="inline.href" target="_blank" rel="noopener noreferrer">{{ inline.text }}</a>
        }
        @case ('br') {
          <br />
        }
      }
    }
  `,
})
export class MarkdownInlines {
  readonly inlines = input.required<MarkdownInline[]>();
}
