import { Component, computed, HostListener, input } from '@angular/core';
import { MarkdownDocument, parseRestrictedMarkdown } from './restricted-markdown';
import { MarkdownBlocks } from './markdown-blocks';

@Component({
  selector: 'vc-markdown-body',
  standalone: true,
  imports: [MarkdownBlocks],
  template: `
    <div class="vc-md" (copy)="onCopy($event)">
      <vc-markdown-blocks [blocks]="document().blocks" [mentionLabels]="mentionLabels()" />
    </div>
  `,
  styles: `
    :host {
      display: block;
    }
    .vc-md :where(p) {
      margin: 0;
      white-space: pre-wrap;
      line-height: 1.45;
      word-break: break-word;
    }
    .vc-md :where(p + p, p + .vc-md__code-block, .vc-md__code-block + p, blockquote, ul, ol) {
      margin-top: 0.45rem;
    }
    .vc-md :where(strong) {
      font-weight: 700;
    }
    .vc-md :where(em) {
      font-style: italic;
    }
    .vc-md :where(del) {
      text-decoration: line-through;
      color: var(--vc-ink-muted);
    }
    .vc-md :where(code) {
      font-family: var(--vc-font-mono);
      font-size: 0.88em;
      background: var(--vc-surface-elevated);
      border-radius: var(--vc-radius-sm);
      padding: 0.05rem 0.3rem;
    }
    .vc-md :where(a) {
      color: var(--vc-brand);
      text-decoration: underline;
      text-underline-offset: 2px;
      word-break: break-all;
    }
    .vc-md :where(blockquote) {
      margin: 0;
      padding-left: 0.65rem;
      border-left: 3px solid var(--vc-border);
      color: var(--vc-ink-muted);
    }
    .vc-md :where(ul, ol) {
      margin: 0;
      padding-left: 1.25rem;
    }
    .vc-md__code-block {
      border: 1px solid var(--vc-border);
      border-radius: var(--vc-radius-sm);
      background: var(--vc-surface-elevated);
      overflow: hidden;
    }
    .vc-md__code-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      gap: 0.5rem;
      padding: 0.25rem 0.55rem;
      border-bottom: 1px solid var(--vc-border);
      font-size: 0.72rem;
      color: var(--vc-ink-subtle);
    }
    .vc-md__code-copy {
      border: 0;
      background: transparent;
      color: var(--vc-brand);
      font: inherit;
      cursor: pointer;
      padding: 0;
    }
    .vc-md__code-block pre {
      margin: 0;
      padding: 0.55rem 0.65rem;
      overflow-x: auto;
    }
    .vc-md__code-block code {
      display: block;
      background: transparent;
      padding: 0;
      font-family: var(--vc-font-mono);
      font-size: 0.82rem;
      line-height: 1.45;
      white-space: pre;
    }
    .sql-kw,
    .js-kw {
      color: color-mix(in srgb, var(--vc-brand) 78%, var(--vc-ink));
      font-weight: 600;
    }
    .json-str {
      color: color-mix(in srgb, var(--vc-success, #2a9d8f) 70%, var(--vc-ink));
    }
    .json-lit {
      color: color-mix(in srgb, var(--vc-brand) 60%, var(--vc-ink));
    }
    .json-num {
      color: color-mix(in srgb, var(--vc-warning, #e9c46a) 70%, var(--vc-ink));
    }
  `,
})
export class MarkdownBody {
  readonly source = input.required<string>();
  readonly mentionLabels = input<Record<string, string>>({});
  readonly document = computed<MarkdownDocument>(() => parseRestrictedMarkdown(this.source()));

  @HostListener('copy', ['$event'])
  onCopy(event: ClipboardEvent): void {
    event.preventDefault();
    void navigator.clipboard.writeText(this.source());
  }
}
