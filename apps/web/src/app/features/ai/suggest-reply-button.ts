import { Component, inject, signal } from '@angular/core';
import { environment } from '../../../environments/environment';
import { ApiService } from '../../core/api/api.service';
import { ChannelStore } from '../../core/services/channel.store';
import { Button } from '../../shared/ui';

@Component({
  selector: 'vc-suggest-reply-button',
  standalone: true,
  imports: [Button],
  template: `
    @if (enabled) {
      <div class="ai-wrap">
        <vc-button variant="subtle" [loading]="loading()" (click)="suggest()">
          Sugerir resposta
        </vc-button>
        @if (suggestion()) {
          <aside class="ai-suggestion" role="status">
            <header>
              <strong>Sugestão</strong>
              <button type="button" (click)="suggestion.set(null)" aria-label="Fechar sugestão">×</button>
            </header>
            <p>{{ suggestion() }}</p>
            <div class="ai-suggestion__actions">
              <vc-button variant="subtle" (click)="useSuggestion()">Usar no composer</vc-button>
            </div>
          </aside>
        }
        @if (error()) {
          <p class="ai-error" role="alert">{{ error() }}</p>
        }
      </div>
    }
  `,
  styles: `
    .ai-wrap {
      position: relative;
    }
    .ai-suggestion {
      position: absolute;
      right: 0;
      top: calc(100% + 0.5rem);
      width: min(22rem, 80vw);
      padding: 0.85rem 1rem;
      border-radius: var(--vc-radius-md);
      border: 1px solid var(--vc-border);
      background: var(--vc-surface-elevated);
      box-shadow: var(--vc-shadow-soft);
      z-index: 5;
      animation: vc-fade-in-up var(--vc-dur-med) var(--vc-ease-out);
    }
    header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 0.35rem;
    }
    header button {
      border: 0;
      background: transparent;
      color: var(--vc-ink-muted);
      font-size: 1.2rem;
      cursor: pointer;
    }
    p {
      margin: 0;
      color: var(--vc-ink-muted);
      line-height: 1.45;
      font-size: 0.92rem;
    }
    .ai-suggestion__actions {
      margin-top: 0.65rem;
      display: flex;
      justify-content: flex-end;
    }
    .ai-error {
      margin: 0.35rem 0 0;
      color: var(--vc-danger);
      font-size: 0.8rem;
    }
  `,
})
export class SuggestReplyButton {
  private readonly api = inject(ApiService);
  private readonly channels = inject(ChannelStore);

  readonly enabled = environment.aiSummarizeEnabled;
  readonly loading = signal(false);
  readonly suggestion = signal<string | null>(null);
  readonly error = signal<string | null>(null);

  async suggest(): Promise<void> {
    const channel = this.channels.activeChannel();
    if (!channel) return;
    this.loading.set(true);
    this.error.set(null);
    try {
      const workspace = this.channels.activeWorkspace();
      if (!workspace) {
        throw new Error('Workspace não selecionado');
      }
      const result = await this.api.suggestChannelReply(workspace.id, channel.id);
      this.suggestion.set(result.suggestion);
    } catch (err) {
      this.error.set(
        err instanceof Error
          ? err.message
          : 'IA indisponível ou desabilitada para este workspace.',
      );
    } finally {
      this.loading.set(false);
    }
  }

  useSuggestion(): void {
    const text = this.suggestion();
    if (!text) return;
    this.channels.prefillComposer(text);
    this.suggestion.set(null);
  }
}
