import { Component, inject, input, output } from '@angular/core';
import { PinStore } from '../../../core/services/pin.store';
import { PinnedMessageItem } from '../../models/chat.models';
import { IconButton } from '../../../shared/ui';

@Component({
  selector: 'vc-pins-panel',
  standalone: true,
  imports: [IconButton],
  template: `
    <section class="pins-panel" aria-label="Mensagens fixadas">
      <header class="pins-panel__header">
        <h2>Mensagens fixadas</h2>
        <vc-icon-button label="Fechar painel" (click)="close.emit()">
          <span aria-hidden="true">×</span>
        </vc-icon-button>
      </header>

      @if (loading()) {
        <p class="pins-panel__status">Carregando…</p>
      } @else if (error()) {
        <p class="pins-panel__status" role="alert">{{ error() }}</p>
      } @else if (!pins().length) {
        <p class="pins-panel__status">Nenhuma mensagem fixada neste canal.</p>
      } @else {
        <ul class="pins-panel__list">
          @for (pin of pins(); track pin.messageId) {
            <li class="pins-panel__item">
              <div class="pins-panel__meta">
                <strong>{{ pin.authorName }}</strong>
                <span>{{ pin.bodyPreview }}</span>
                <small>Fixada por {{ pin.pinnedByName }}</small>
              </div>
              <div class="pins-panel__actions">
                <button type="button" (click)="jump.emit(pin)">Ir até</button>
                @if (canUnpin()) {
                  <button type="button" (click)="unpin.emit(pin)">Desafixar</button>
                }
              </div>
            </li>
          }
        </ul>
      }
    </section>
  `,
  styles: `
    .pins-panel {
      display: flex;
      flex-direction: column;
      gap: var(--vc-space-3);
      height: 100%;
    }

    .pins-panel__header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--vc-space-2);
    }

    .pins-panel__header h2 {
      margin: 0;
      font-size: var(--vc-text-sm);
      font-weight: 600;
    }

    .pins-panel__status {
      margin: 0;
      color: var(--vc-text-muted);
      font-size: var(--vc-text-sm);
    }

    .pins-panel__list {
      list-style: none;
      margin: 0;
      padding: 0;
      display: flex;
      flex-direction: column;
      gap: var(--vc-space-2);
      overflow: auto;
    }

    .pins-panel__item {
      border: 1px solid var(--vc-border-subtle);
      border-radius: var(--vc-radius-md);
      padding: var(--vc-space-2);
      display: flex;
      flex-direction: column;
      gap: var(--vc-space-2);
    }

    .pins-panel__meta {
      display: flex;
      flex-direction: column;
      gap: var(--vc-space-1);
      font-size: var(--vc-text-sm);
    }

    .pins-panel__meta span {
      color: var(--vc-text-muted);
    }

    .pins-panel__meta small {
      color: var(--vc-text-muted);
      font-size: var(--vc-text-xs);
    }

    .pins-panel__actions {
      display: flex;
      gap: var(--vc-space-2);
    }

    .pins-panel__actions button {
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
export class PinsPanel {
  private readonly pinStore = inject(PinStore);

  readonly canUnpin = input(true);
  readonly close = output<void>();
  readonly jump = output<PinnedMessageItem>();
  readonly unpin = output<PinnedMessageItem>();

  readonly pins = this.pinStore.activePins;
  readonly loading = this.pinStore.loading;
  readonly error = this.pinStore.error;
}
