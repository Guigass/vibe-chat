import { Component, computed, inject, input, output, signal } from '@angular/core';
import { Channel } from '../../../shared/models/chat.models';
import { ChannelStore } from '../../../core/services/channel.store';

const MAX_TARGETS = 5;

@Component({
  selector: 'vc-forward-dialog',
  standalone: true,
  template: `
    @if (open()) {
      <div class="fwd-backdrop" (click)="cancel.emit()"></div>
      <div class="fwd" role="dialog" aria-modal="true" aria-labelledby="fwd-title">
        <header class="fwd__header">
          <h2 id="fwd-title">Encaminhar mensagem</h2>
          <button type="button" class="ghost" (click)="cancel.emit()" aria-label="Fechar">×</button>
        </header>

        <label class="fwd__search">
          <span class="sr-only">Buscar destino</span>
          <input
            type="search"
            [value]="query()"
            (input)="query.set(($any($event.target).value))"
            placeholder="Buscar canal ou DM…"
            autofocus
          />
        </label>

        @if (selected().length) {
          <ul class="fwd__chips" aria-label="Destinos selecionados">
            @for (ch of selected(); track ch.id) {
              <li>
                <button type="button" (click)="toggle(ch)" [attr.aria-label]="'Remover ' + label(ch)">
                  {{ label(ch) }} ×
                </button>
              </li>
            }
          </ul>
        }

        <ul class="fwd__list" role="listbox" aria-label="Destinos">
          @for (ch of filtered(); track ch.id) {
            <li>
              <button
                type="button"
                role="option"
                [attr.aria-selected]="isSelected(ch.id)"
                [disabled]="!isSelected(ch.id) && selected().length >= maxTargets"
                (click)="toggle(ch)"
              >
                <span>{{ label(ch) }}</span>
                @if (ch.isPrivate) {
                  <span class="fwd__meta">privado</span>
                }
              </button>
            </li>
          } @empty {
            <li class="fwd__empty">Nenhum destino encontrado</li>
          }
        </ul>

        @if (privacyWarning()) {
          <p class="fwd__warn" role="status">
            Você está encaminhando de um canal privado para um público — o conteúdo ficará
            visível a mais pessoas.
          </p>
        }

        <label class="fwd__comment">
          <span>Comentário (opcional)</span>
          <textarea
            rows="2"
            [value]="comment()"
            (input)="comment.set(($any($event.target).value))"
            maxlength="8000"
          ></textarea>
        </label>

        <footer class="fwd__footer">
          <button type="button" class="ghost" (click)="cancel.emit()">Cancelar</button>
          <button
            type="button"
            [disabled]="selected().length === 0 || submitting()"
            (click)="submit()"
          >
            Encaminhar{{ selected().length ? ' (' + selected().length + ')' : '' }}
          </button>
        </footer>
      </div>
    }
  `,
  styles: `
    .fwd-backdrop {
      position: fixed;
      inset: 0;
      background: color-mix(in srgb, var(--vc-ink) 40%, transparent);
      z-index: 40;
    }
    .fwd {
      position: fixed;
      z-index: 41;
      top: 50%;
      left: 50%;
      transform: translate(-50%, -50%);
      width: min(28rem, calc(100vw - 2rem));
      max-height: min(36rem, calc(100vh - 2rem));
      display: flex;
      flex-direction: column;
      gap: 0.75rem;
      padding: 1rem 1.1rem;
      border-radius: var(--vc-radius-md);
      background: var(--vc-surface);
      border: 1px solid var(--vc-border);
      box-shadow: var(--vc-shadow-md, 0 12px 40px color-mix(in srgb, var(--vc-ink) 25%, transparent));
    }
    .fwd__header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 0.5rem;
    }
    .fwd__header h2 {
      margin: 0;
      font-size: 1.05rem;
      font-family: var(--vc-font-display);
    }
    .fwd__search input,
    .fwd__comment textarea {
      width: 100%;
      box-sizing: border-box;
      padding: 0.5rem 0.65rem;
      border-radius: var(--vc-radius-sm);
      border: 1px solid var(--vc-border);
      background: var(--vc-bg);
      color: inherit;
      font: inherit;
    }
    .fwd__chips {
      list-style: none;
      margin: 0;
      padding: 0;
      display: flex;
      flex-wrap: wrap;
      gap: 0.35rem;
    }
    .fwd__chips button {
      border: 1px solid var(--vc-border);
      background: color-mix(in srgb, var(--vc-brand) 12%, transparent);
      border-radius: var(--vc-radius-sm);
      padding: 0.2rem 0.45rem;
      font-size: 0.85rem;
      cursor: pointer;
      color: inherit;
    }
    .fwd__list {
      list-style: none;
      margin: 0;
      padding: 0;
      overflow: auto;
      min-height: 8rem;
      max-height: 14rem;
      border: 1px solid var(--vc-border);
      border-radius: var(--vc-radius-sm);
    }
    .fwd__list button {
      width: 100%;
      text-align: left;
      display: flex;
      justify-content: space-between;
      gap: 0.5rem;
      padding: 0.55rem 0.7rem;
      border: 0;
      background: transparent;
      color: inherit;
      cursor: pointer;
      font: inherit;
    }
    .fwd__list button[aria-selected='true'] {
      background: color-mix(in srgb, var(--vc-brand) 14%, transparent);
    }
    .fwd__list button:disabled {
      opacity: 0.45;
      cursor: not-allowed;
    }
    .fwd__meta {
      color: var(--vc-text-muted);
      font-size: 0.78rem;
    }
    .fwd__empty {
      padding: 0.85rem;
      color: var(--vc-text-muted);
    }
    .fwd__warn {
      margin: 0;
      font-size: 0.85rem;
      color: var(--vc-warning, #b45309);
    }
    .fwd__comment {
      display: grid;
      gap: 0.35rem;
      font-size: 0.9rem;
    }
    .fwd__footer {
      display: flex;
      justify-content: flex-end;
      gap: 0.5rem;
    }
    .fwd__footer button,
    .fwd__header button {
      border: 1px solid var(--vc-border);
      background: var(--vc-brand);
      color: var(--vc-on-brand, #fff);
      border-radius: var(--vc-radius-sm);
      padding: 0.45rem 0.85rem;
      cursor: pointer;
      font: inherit;
    }
    .fwd__footer button:disabled {
      opacity: 0.5;
      cursor: not-allowed;
    }
    .fwd__footer .ghost,
    .fwd__header .ghost {
      background: transparent;
      color: inherit;
    }
    .sr-only {
      position: absolute;
      width: 1px;
      height: 1px;
      padding: 0;
      margin: -1px;
      overflow: hidden;
      clip: rect(0, 0, 0, 0);
      border: 0;
    }
  `,
})
export class ForwardDialog {
  private readonly channels = inject(ChannelStore);

  readonly open = input(false);
  readonly sourceIsPrivate = input(false);
  readonly submitting = input(false);
  readonly cancel = output<void>();
  readonly confirm = output<{ targetChannelIds: string[]; comment: string }>();

  readonly maxTargets = MAX_TARGETS;
  readonly query = signal('');
  readonly comment = signal('');
  readonly selected = signal<Channel[]>([]);

  readonly filtered = computed(() => {
    const q = this.query().trim().toLowerCase();
    const all = this.channels.channels();
    return all.filter((ch) => {
      if (!q) return true;
      return this.label(ch).toLowerCase().includes(q);
    });
  });

  readonly privacyWarning = computed(() => {
    if (!this.sourceIsPrivate()) return false;
    return this.selected().some((ch) => !ch.isPrivate && !ch.isDirect);
  });

  label(ch: Channel): string {
    if (ch.isDirect) {
      return ch.peerDisplayName || ch.name || 'DM';
    }
    return ch.name.startsWith('#') ? ch.name : `#${ch.name}`;
  }

  isSelected(id: string): boolean {
    return this.selected().some((c) => c.id === id);
  }

  toggle(ch: Channel): void {
    const current = this.selected();
    if (current.some((c) => c.id === ch.id)) {
      this.selected.set(current.filter((c) => c.id !== ch.id));
      return;
    }
    if (current.length >= MAX_TARGETS) return;
    this.selected.set([...current, ch]);
  }

  submit(): void {
    const ids = this.selected().map((c) => c.id);
    if (!ids.length) return;
    this.confirm.emit({ targetChannelIds: ids, comment: this.comment().trim() });
  }

  reset(): void {
    this.query.set('');
    this.comment.set('');
    this.selected.set([]);
  }
}
