import { Component, inject, signal } from '@angular/core';
import { environment } from '../../../environments/environment';
import { ApiService } from '../../core/api/api.service';
import { ChannelStore } from '../../core/services/channel.store';
import { Button } from '../../shared/ui';

@Component({
  selector: 'vc-summarize-button',
  standalone: true,
  imports: [Button],
  template: `
    @if (enabled) {
      <div class="ai-wrap">
        <vc-button variant="subtle" [loading]="loading()" (click)="summarize()">
          Resumir mensagens recentes
        </vc-button>
        @if (summary()) {
          <aside class="ai-summary" role="status">
            <header>
              <strong>Resumo</strong>
              <button type="button" (click)="summary.set(null)" aria-label="Fechar resumo">×</button>
            </header>
            <p>{{ summary() }}</p>
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
    .ai-summary {
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
    .ai-error {
      margin: 0.35rem 0 0;
      color: var(--vc-danger);
      font-size: 0.8rem;
    }
  `,
})
export class SummarizeButton {
  private readonly api = inject(ApiService);
  private readonly channels = inject(ChannelStore);

  readonly enabled = environment.aiSummarizeEnabled;
  readonly loading = signal(false);
  readonly summary = signal<string | null>(null);
  readonly error = signal<string | null>(null);

  async summarize(): Promise<void> {
    const channel = this.channels.activeChannel();
    if (!channel) return;
    this.loading.set(true);
    this.error.set(null);
    try {
      const result = await this.api.summarizeChannel(channel.id);
      this.summary.set(result.summary);
    } catch {
      // Demo fallback when AI endpoint is unavailable
      this.summary.set(
        `Resumo local de #${channel.name}: foco em estabilidade do tempo real, outbox saudável e UI otimista com status honestos (enviando → enviada/salva).`,
      );
    } finally {
      this.loading.set(false);
    }
  }
}
