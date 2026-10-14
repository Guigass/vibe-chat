import { Component, inject, signal } from '@angular/core';
import { ApiService } from '../core/api/api.service';
import { ui } from '../core/i18n/strings';
import { ChannelStore } from '../core/services/channel.store';
import { MessageStore } from '../core/services/message.store';
import { SemanticCitation } from '../shared/models/chat.models';
import { Button, Input } from '../shared/ui';

@Component({
  selector: 'vc-semantic-search',
  standalone: true,
  imports: [Button, Input],
  template: `
    @if (!channels.isGuest()) {
      <div class="semantic">
        <vc-button variant="subtle" data-testid="search-semantic" (click)="toggle()">
          {{ ui.searchSemanticLabel }}
        </vc-button>
        @if (open()) {
          <div class="semantic__panel" role="dialog" [attr.aria-label]="ui.searchSemanticLabel">
            <vc-input
              controlId="vc-semantic-query"
              [(value)]="query"
              [placeholder]="ui.searchSemanticLabel"
              [ariaLabel]="ui.searchSemanticLabel"
            />
            <vc-button variant="subtle" (click)="ask()">{{ ui.searchSemantic }}</vc-button>
            @if (loading()) {
              <p class="semantic__status">{{ ui.searchLoading }}</p>
            } @else if (error()) {
              <p class="semantic__status" role="alert">{{ error() }}</p>
            } @else if (evidence() === 'none') {
              <p class="semantic__status">{{ ui.searchSemanticEmpty }}</p>
            } @else if (answer()) {
              <p class="semantic__label">{{ ui.searchSemanticAnswer }}</p>
              <p class="semantic__answer">{{ answer() }}</p>
              @for (citation of citations(); track citation.chunkId; let index = $index) {
                <button type="button" class="semantic__cite" (click)="openCitation(citation)">
                  <span>[{{ index + 1 }}]</span>
                  {{ citation.excerpt }}
                </button>
              }
              <vc-button variant="subtle" (click)="report()">
                {{ reported() ? ui.searchSemanticReported : ui.searchSemanticReport }}
              </vc-button>
            }
          </div>
        }
      </div>
    }
  `,
  styles: `
    .semantic { position: relative; }
    .semantic__panel {
      position: absolute;
      right: 0;
      top: calc(100% + 0.35rem);
      z-index: 5;
      width: min(22rem, 80vw);
      display: grid;
      gap: 0.35rem;
      padding: 0.6rem;
      border: 1px solid var(--vc-border);
      border-radius: var(--vc-radius-sm);
      background: var(--vc-surface);
    }
    .semantic__label, .semantic__status {
      margin: 0;
      color: var(--vc-ink-muted);
      font-size: 0.75rem;
    }
    .semantic__answer {
      margin: 0;
      color: var(--vc-ink);
      font-size: 0.86rem;
      white-space: pre-line;
    }
    .semantic__cite {
      border: 0;
      border-radius: var(--vc-radius-sm);
      background: transparent;
      color: var(--vc-ink);
      text-align: left;
      padding: 0.35rem 0.2rem;
      cursor: pointer;
    }
    .semantic__cite:hover, .semantic__cite:focus-visible {
      background: color-mix(in srgb, var(--vc-brand) 12%, transparent);
    }
  `,
})
export class SemanticSearchPanel {
  protected readonly ui = ui;
  protected readonly channels = inject(ChannelStore);
  private readonly api = inject(ApiService);
  private readonly messages = inject(MessageStore);
  protected readonly open = signal(false);
  protected readonly query = signal('');
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly evidence = signal<'cited' | 'none' | null>(null);
  protected readonly answer = signal<string | null>(null);
  protected readonly citations = signal<SemanticCitation[]>([]);
  protected readonly reported = signal(false);

  protected toggle(): void {
    this.open.update((value) => !value);
  }

  protected async ask(): Promise<void> {
    const workspaceId = this.channels.activeWorkspace()?.id;
    const term = this.query().trim();
    if (!workspaceId || term.length < 2) {
      this.evidence.set('none');
      this.answer.set(null);
      this.citations.set([]);
      return;
    }
    this.loading.set(true);
    this.error.set(null);
    try {
      const result = await this.api.semanticQuery(workspaceId, term);
      this.evidence.set(result.evidence);
      this.answer.set(result.answer);
      this.citations.set(result.citations ?? []);
      this.reported.set(false);
    } catch (err) {
      const text = err instanceof Error ? err.message : '';
      this.evidence.set(null);
      this.answer.set(null);
      this.citations.set([]);
      if (text.includes('SemanticSearchDisabled')) {
        this.error.set(ui.searchSemanticDisabled);
      } else if (text.includes('SemanticBudgetExceeded') || text.includes('SemanticRateLimited')) {
        this.error.set(ui.searchSemanticLimited);
      } else {
        this.error.set(ui.searchFailed);
      }
    } finally {
      this.loading.set(false);
    }
  }

  protected async openCitation(citation: SemanticCitation): Promise<void> {
    if (citation.sourceKind !== 'message' || !citation.channelId || !citation.sequence) {
      return;
    }
    await this.channels.selectChannel(citation.channelId);
    await this.messages.jumpToSequence(citation.channelId, citation.sequence, citation.sourceId);
    this.open.set(false);
  }

  protected async report(): Promise<void> {
    const workspaceId = this.channels.activeWorkspace()?.id;
    const ids = this.citations().map((item) => item.chunkId);
    if (!workspaceId || ids.length === 0) {
      return;
    }
    try {
      await this.api.reportSemantic(workspaceId, ids);
      this.reported.set(true);
    } catch {
      this.error.set(ui.searchFailed);
    }
  }
}
