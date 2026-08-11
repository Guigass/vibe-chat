import { Component, inject, signal } from '@angular/core';
import { SavedStore } from '../../../core/services/saved.store';
import { SavedMessageItem } from '../../../shared/models/chat.models';
import { IconButton } from '../../../shared/ui';

@Component({
  selector: 'vc-saved-panel',
  standalone: true,
  imports: [IconButton],
  template: `
    <section class="saved-panel" aria-label="Mensagens salvas">
      <header class="saved-panel__header">
        <h2>Salvos</h2>
        <vc-icon-button label="Fechar painel" (click)="saved.closePanel()">
          <span aria-hidden="true">×</span>
        </vc-icon-button>
      </header>

      <div class="saved-panel__filters" role="tablist" aria-label="Filtro de salvos">
        <button
          type="button"
          role="tab"
          [attr.aria-selected]="!saved.showCompleted()"
          [class.is-active]="!saved.showCompleted()"
          (click)="saved.setShowCompleted(false)"
        >
          Pendentes
        </button>
        <button
          type="button"
          role="tab"
          [attr.aria-selected]="saved.showCompleted()"
          [class.is-active]="saved.showCompleted()"
          (click)="saved.setShowCompleted(true)"
        >
          Concluídos
        </button>
      </div>

      @if (saved.loading()) {
        <p class="saved-panel__status">Carregando…</p>
      } @else if (saved.error()) {
        <p class="saved-panel__status" role="alert">{{ saved.error() }}</p>
      } @else if (!saved.items().length) {
        <p class="saved-panel__status">
          @if (saved.showCompleted()) {
            Nenhum salvo concluído.
          } @else {
            Salve mensagens para voltar depois — só você vê esta lista.
          }
        </p>
      } @else {
        <ul class="saved-panel__list">
          @for (item of saved.items(); track item.messageId) {
            <li class="saved-panel__item">
              <div class="saved-panel__meta">
                <strong>
                  {{ formatOrigin(item) }}
                  · {{ item.authorName || '—' }}
                </strong>
                <span>{{ item.bodyPreview }}</span>
                @if (item.note) {
                  <small class="saved-panel__note">{{ item.note }}</small>
                }
              </div>
              <div class="saved-panel__note-edit">
                <input
                  type="text"
                  maxlength="280"
                  [value]="noteDrafts()[item.messageId] ?? item.note ?? ''"
                  (input)="onNoteInput(item.messageId, $event)"
                  (change)="onNoteCommit(item)"
                  [attr.aria-label]="'Nota do salvo'"
                  placeholder="Nota pessoal (opcional)"
                />
              </div>
              <div class="saved-panel__actions">
                @if (!item.messageRemoved) {
                  <button type="button" (click)="saved.jumpToSaved(item)">Ir até</button>
                }
                <button type="button" (click)="saved.toggleComplete(item)">
                  {{ item.completedAt ? 'Reabrir' : 'Concluir' }}
                </button>
                <button type="button" (click)="saved.unsaveMessage(item.messageId)">Remover</button>
              </div>
            </li>
          }
        </ul>
      }
    </section>
  `,
  styles: `
    .saved-panel {
      display: flex;
      flex-direction: column;
      gap: var(--vc-space-3);
      height: 100%;
    }

    .saved-panel__header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--vc-space-2);
    }

    .saved-panel__header h2 {
      margin: 0;
      font-size: var(--vc-text-sm);
      font-weight: 600;
    }

    .saved-panel__filters {
      display: flex;
      gap: var(--vc-space-2);
    }

    .saved-panel__filters button {
      font: inherit;
      font-size: var(--vc-text-xs);
      padding: var(--vc-space-1) var(--vc-space-2);
      border-radius: var(--vc-radius-sm);
      border: 1px solid var(--vc-border-subtle);
      background: transparent;
      color: var(--vc-text-muted);
      cursor: pointer;
    }

    .saved-panel__filters button.is-active {
      color: var(--vc-text);
      border-color: var(--vc-border);
      background: var(--vc-surface-raised);
    }

    .saved-panel__status {
      margin: 0;
      color: var(--vc-text-muted);
      font-size: var(--vc-text-sm);
    }

    .saved-panel__list {
      list-style: none;
      margin: 0;
      padding: 0;
      display: flex;
      flex-direction: column;
      gap: var(--vc-space-2);
      overflow: auto;
    }

    .saved-panel__item {
      border-bottom: 1px solid var(--vc-border-subtle);
      padding-bottom: var(--vc-space-2);
      display: flex;
      flex-direction: column;
      gap: var(--vc-space-2);
    }

    .saved-panel__meta {
      display: flex;
      flex-direction: column;
      gap: var(--vc-space-1);
      font-size: var(--vc-text-sm);
    }

    .saved-panel__meta span {
      color: var(--vc-text-muted);
    }

    .saved-panel__note {
      color: var(--vc-text-muted);
      font-size: var(--vc-text-xs);
    }

    .saved-panel__note-edit input {
      width: 100%;
      font: inherit;
      font-size: var(--vc-text-xs);
      padding: var(--vc-space-1) var(--vc-space-2);
      border-radius: var(--vc-radius-sm);
      border: 1px solid var(--vc-border-subtle);
      background: var(--vc-surface);
      color: var(--vc-text);
    }

    .saved-panel__actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--vc-space-2);
    }

    .saved-panel__actions button {
      font: inherit;
      font-size: var(--vc-text-xs);
      padding: var(--vc-space-1) var(--vc-space-2);
      border-radius: var(--vc-radius-sm);
      border: 1px solid var(--vc-border-subtle);
      background: var(--vc-surface-raised);
      color: var(--vc-text);
      cursor: pointer;
    }
  `,
})
export class SavedPanel {
  readonly saved = inject(SavedStore);
  readonly noteDrafts = signal<Record<string, string>>({});

  formatOrigin(item: SavedMessageItem): string {
    const raw = (item.channelName ?? '').trim();
    const isDirect =
      item.channelType === 'Direct' || /^dm:/i.test(raw) || raw === 'DM';
    if (isDirect) {
      if (!raw || raw === 'DM' || /^dm:/i.test(raw)) return '@DM';
      return raw.startsWith('@') ? raw : `@${raw}`;
    }
    if (!raw) return '#';
    if (raw.startsWith('#') || raw.startsWith('@')) return raw;
    return `#${raw}`;
  }

  onNoteInput(messageId: string, event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.noteDrafts.update((current) => ({ ...current, [messageId]: value }));
  }

  onNoteCommit(item: SavedMessageItem): void {
    const draft = this.noteDrafts()[item.messageId];
    if (draft === undefined || draft === (item.note ?? '')) return;
    void this.saved.updateNote(item, draft);
  }
}
