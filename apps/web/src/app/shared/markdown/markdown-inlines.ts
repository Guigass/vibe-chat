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
          <strong><vc-markdown-inlines [inlines]="inline.children" [mentionLabels]="mentionLabels()" /></strong>
        }
        @case ('em') {
          <em><vc-markdown-inlines [inlines]="inline.children" [mentionLabels]="mentionLabels()" /></em>
        }
        @case ('del') {
          <del><vc-markdown-inlines [inlines]="inline.children" [mentionLabels]="mentionLabels()" /></del>
        }
        @case ('code') {
          <code>{{ inline.text }}</code>
        }
        @case ('mention') {
          <span class="vc-md__mention">{{ labelFor(inline) }}</span>
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
  styles: `
    .vc-md__mention {
      display: inline-flex;
      align-items: center;
      padding: 0 0.35rem;
      border-radius: var(--vc-radius-sm);
      background: color-mix(in srgb, var(--vc-brand) 18%, transparent);
      color: var(--vc-brand);
      font-weight: 600;
    }
  `,
})
export class MarkdownInlines {
  readonly inlines = input.required<MarkdownInline[]>();
  readonly mentionLabels = input<Record<string, string>>({});

  labelFor(inline: Extract<MarkdownInline, { kind: 'mention' }>): string {
    if (inline.special === 'here') return '@aqui';
    if (inline.special === 'channel') return '@canal';
    const name = this.mentionLabels()[inline.userId ?? ''];
    return name ? `@${name}` : '@usuário';
  }
}
