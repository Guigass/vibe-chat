import { Component, input, output } from '@angular/core';
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
          <strong>
            <vc-markdown-inlines
              [inlines]="inline.children"
              [mentionLabels]="mentionLabels()"
              (mentionClick)="mentionClick.emit($event)"
            />
          </strong>
        }
        @case ('em') {
          <em>
            <vc-markdown-inlines
              [inlines]="inline.children"
              [mentionLabels]="mentionLabels()"
              (mentionClick)="mentionClick.emit($event)"
            />
          </em>
        }
        @case ('del') {
          <del>
            <vc-markdown-inlines
              [inlines]="inline.children"
              [mentionLabels]="mentionLabels()"
              (mentionClick)="mentionClick.emit($event)"
            />
          </del>
        }
        @case ('code') {
          <code>{{ inline.text }}</code>
        }
        @case ('mention') {
          @if (clickable(inline); as userId) {
            <button
              type="button"
              class="vc-md__mention"
              (click)="onMentionClick($event, userId)"
            >
              {{ labelFor(inline) }}
            </button>
          } @else {
            <span class="vc-md__mention" [class.vc-md__mention--plain]="!!inline.userId">
              {{ labelFor(inline) }}
            </span>
          }
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
      margin: 0;
      padding: 0 0.35rem;
      border: 0;
      border-radius: var(--vc-radius-sm);
      background: color-mix(in srgb, var(--vc-brand) 18%, transparent);
      color: var(--vc-brand);
      font: inherit;
      font-weight: 600;
      line-height: inherit;
    }
    button.vc-md__mention {
      cursor: pointer;
    }
    button.vc-md__mention:hover,
    button.vc-md__mention:focus-visible {
      background: color-mix(in srgb, var(--vc-brand) 28%, transparent);
      outline: none;
    }
    .vc-md__mention--plain {
      background: transparent;
      color: var(--vc-ink-muted);
      font-weight: 500;
      padding: 0;
    }
  `,
})
export class MarkdownInlines {
  readonly inlines = input.required<MarkdownInline[]>();
  readonly mentionLabels = input<Record<string, string>>({});
  readonly mentionClick = output<string>();

  clickable(inline: Extract<MarkdownInline, { kind: 'mention' }>): string | null {
    const userId = inline.userId;
    if (!userId || !this.mentionLabels()[userId]) return null;
    return userId;
  }

  onMentionClick(event: Event, userId: string): void {
    event.preventDefault();
    event.stopPropagation();
    this.mentionClick.emit(userId);
  }

  labelFor(inline: Extract<MarkdownInline, { kind: 'mention' }>): string {
    if (inline.special === 'here') return '@aqui';
    if (inline.special === 'channel') return '@canal';
    const name = this.mentionLabels()[inline.userId ?? ''];
    return name ? `@${name}` : '@usuário';
  }
}
