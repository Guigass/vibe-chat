import { Component, computed, inject, input, signal } from '@angular/core';
import { ApiService } from '../../../core/api/api.service';
import { fillTemplate, translateErrorCode, ui } from '../../../core/i18n/strings';
import { ChannelStore } from '../../../core/services/channel.store';
import { ChatMessage, MessagePluginCard } from '../../models/chat.models';

@Component({
  selector: 'vc-plugin-action-bar',
  standalone: true,
  template: `
    @if (card(); as plugin) {
      <section class="plugin-actions" [attr.aria-label]="label()">
        <p class="plugin-actions__by">{{ label() }}</p>
        @if (expired()) {
          <p class="plugin-actions__state">{{ ui.pluginActionExpired }}</p>
        } @else {
          @for (action of plugin.actions; track action) {
            <button
              type="button"
              class="plugin-actions__button"
              [disabled]="busy() || accepted() === action"
              (click)="run(action)"
            >
              {{ buttonLabel(action) }}
            </button>
          }
        }
        @if (error()) {
          <p class="plugin-actions__state" role="alert">{{ error() }}</p>
        }
      </section>
    }
  `,
  styles: `
    .plugin-actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--vc-space-2);
      align-items: center;
      margin-top: var(--vc-space-2);
    }
    .plugin-actions__by,
    .plugin-actions__state {
      margin: 0;
      color: var(--vc-ink-muted, var(--vc-ink));
      font-size: 0.85rem;
    }
    .plugin-actions__button {
      border: 1px solid var(--vc-border);
      background: transparent;
      color: var(--vc-ink);
      border-radius: 999px;
      padding: 0.2rem 0.7rem;
      font: inherit;
      cursor: pointer;
    }
    .plugin-actions__button:disabled {
      cursor: default;
      opacity: 0.6;
    }
  `,
})
export class PluginActionBar {
  readonly message = input.required<ChatMessage>();
  readonly card = input.required<MessagePluginCard>();
  readonly ui = ui;

  private readonly api = inject(ApiService);
  private readonly channels = inject(ChannelStore);
  private readonly keys = new Map<string, string>();

  readonly busy = signal(false);
  readonly accepted = signal<string | null>(null);
  readonly error = signal<string | null>(null);
  readonly label = computed(() => fillTemplate(ui.pluginActionBy, { name: this.card().pluginName }));
  readonly expired = computed(() => Date.parse(this.card().expiresAt) <= Date.now());

  buttonLabel(action: string): string {
    return action.endsWith('/ack') ? ui.pluginActionAck : fillTemplate(ui.pluginActionNamed, { action });
  }

  async run(action: string): Promise<void> {
    const workspaceId = this.channels.activeWorkspace()?.id;
    if (!workspaceId || this.busy() || this.expired()) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    try {
      const result = await this.api.invokePluginAction(workspaceId, this.message().channelId, this.message().id, {
        action,
        idempotencyKey: this.keyFor(action),
      });
      if (result.status === 'Accepted') {
        this.accepted.set(action);
      }
    } catch (error) {
      this.error.set(translateErrorCode(readErrorCode(error)) || ui.pluginActionFailed);
    } finally {
      this.busy.set(false);
    }
  }

  private keyFor(action: string): string {
    const existing = this.keys.get(action);
    if (existing) {
      return existing;
    }
    const key = crypto.randomUUID();
    this.keys.set(action, key);
    return key;
  }
}

function readErrorCode(error: unknown): string {
  const text = error instanceof Error ? error.message : '';
  try {
    const parsed = JSON.parse(text) as { error?: string };
    return parsed.error ?? '';
  } catch {
    return '';
  }
}
