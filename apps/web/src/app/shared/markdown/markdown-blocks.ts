import { Component, input, signal } from '@angular/core';
import { highlightCode, MarkdownBlock } from './restricted-markdown';
import { MarkdownInlines } from './markdown-inlines';

@Component({
  selector: 'vc-markdown-blocks',
  standalone: true,
  imports: [MarkdownBlocks, MarkdownInlines],
  template: `
    @for (block of blocks(); track $index) {
      @switch (block.kind) {
        @case ('paragraph') {
          <p><vc-markdown-inlines [inlines]="block.inlines" /></p>
        }
        @case ('code') {
          <div class="vc-md__code-block">
            <div class="vc-md__code-header">
              @if (block.language) {
                <span class="vc-md__code-lang">{{ block.language }}</span>
              }
              <button type="button" class="vc-md__code-copy" (click)="copyCode(block.text)">
                {{ copied() === block.text ? 'Copiado' : 'Copiar' }}
              </button>
            </div>
            <pre><code>
              @for (token of tokensFor(block); track $index) {
                @if (token.className) {
                  <span [class]="token.className">{{ token.text }}</span>
                } @else {
                  {{ token.text }}
                }
              }
            </code></pre>
          </div>
        }
        @case ('quote') {
          <blockquote><vc-markdown-blocks [blocks]="block.blocks" /></blockquote>
        }
        @case ('ul') {
          <ul>
            @for (item of block.items; track $index) {
              <li><vc-markdown-blocks [blocks]="item" /></li>
            }
          </ul>
        }
        @case ('ol') {
          <ol>
            @for (item of block.items; track $index) {
              <li><vc-markdown-blocks [blocks]="item" /></li>
            }
          </ol>
        }
      }
    }
  `,
})
export class MarkdownBlocks {
  readonly blocks = input.required<MarkdownBlock[]>();
  readonly copied = signal<string | null>(null);

  tokensFor(block: Extract<MarkdownBlock, { kind: 'code' }>) {
    return highlightCode(block.language, block.text);
  }

  async copyCode(text: string): Promise<void> {
    await navigator.clipboard.writeText(text);
    this.copied.set(text);
  }
}
