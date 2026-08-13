import { Component, computed, effect, ElementRef, inject, signal, untracked, viewChild } from '@angular/core';
import { ThreadStore } from '../../../core/services/thread.store';
import { replyPreviewText } from '../../../core/services/message-sync';
import { MessageStore } from '../../../core/services/message.store';
import { ChatHubService } from '../../../core/services/chat-hub.service';
import { DraftStoreService } from '../../../core/services/draft-store.service';
import { threadConversationId } from '../../../core/services/draft-storage';
import { Button, EmptyState, IconButton, MessageBubble, Skeleton, Textarea } from '../../../shared/ui';
import {
  ChatMessage,
  isMessageBodyTooLong,
  measureMessageBodyLength,
  MESSAGE_BODY_COUNTER_THRESHOLD,
  MESSAGE_BODY_MAX_LENGTH,
} from '../../../shared/models/chat.models';
import { updateTextareaSelection } from '../../../shared/markdown/markdown-format';

@Component({
  selector: 'vc-thread-panel',
  standalone: true,
  imports: [Button, EmptyState, IconButton, MessageBubble, Skeleton, Textarea],
  template: `
    <div class="thread">
      <header class="thread__header">
        <div>
          <h2>Thread</h2>
          @if (threads.active(); as active) {
            <p>{{ active.replyCount }} {{ active.replyCount === 1 ? 'resposta' : 'respostas' }}</p>
          }
        </div>
        <vc-icon-button label="Fechar thread" (click)="threads.close()">
          <span aria-hidden="true">×</span>
        </vc-icon-button>
      </header>

      <div class="thread__scroll" #scroller>
        @if (threads.loading()) {
          <div class="thread__loading">
            <vc-skeleton height="3rem" />
            <vc-skeleton height="3rem" width="85%" />
          </div>
        } @else if (threads.active(); as active) {
          @if (active.parentMessage; as parent) {
            <div class="thread__parent">
              <vc-message-bubble
                [message]="parent"
                [showReplyAction]="true"
                [highlighted]="messages.highlightMessageId() === parent.id"
                (reply)="onReply(parent)"
                (startEdit)="onStartEdit(parent)"
                (quoteClick)="onQuoteClick($event)"
                (react)="onReact(parent.id, $event)"
                (removeLinkPreview)="onRemoveLinkPreview(parent.id)"
              />
            </div>
          }

          @if (!threads.sortedMessages().length) {
            <vc-empty-state
              title="Sem respostas ainda"
              description="Escreva abaixo para continuar a conversa nesta thread."
            />
          } @else {
            <div class="thread__list">
              @for (message of threads.sortedMessages(); track message.id) {
                <vc-message-bubble
                  [message]="message"
                  [showReplyAction]="true"
                  [highlighted]="messages.highlightMessageId() === message.id"
                  (reply)="onReply(message)"
                  (startEdit)="onStartEdit(message)"
                  (quoteClick)="onQuoteClick($event)"
                  (react)="onReact(message.id, $event)"
                  (removeLinkPreview)="onRemoveLinkPreview(message.id)"
                />
              }
            </div>
          }
        }
      </div>

      <form class="thread__composer" (submit)="onSubmit($event)">
        @if (threads.replyTarget(); as cite) {
          <div class="thread__reply" role="status">
            <div class="thread__reply-meta">
              <strong>Respondendo a {{ cite.authorName }}</strong>
              <span>{{ citePreview(cite.body) }}</span>
            </div>
            <button
              type="button"
              class="ghost"
              aria-label="Cancelar citação"
              (click)="threads.clearReplyTarget()"
            >
              ×
            </button>
          </div>
        }
        @if (threads.editingMessage(); as editing) {
          <div class="thread__reply" role="status">
            <div class="thread__reply-meta">
              <strong>Editando mensagem</strong>
              <span>{{ citePreview(editing.body) }}</span>
            </div>
            <button
              type="button"
              class="ghost"
              aria-label="Cancelar edição"
              (click)="cancelEdit()"
            >
              ×
            </button>
          </div>
        }
        <vc-textarea
          #threadTextarea
          [(value)]="draft"
          [placeholder]="threads.editingMessage() ? 'Editar mensagem' : 'Responder na thread…'"
          [label]="''"
          (keydown)="onKeydown($event)"
        />
        @if (showCounter()) {
          <p
            class="thread__counter"
            [class.thread__counter--over]="bodyTooLong()"
            aria-live="polite"
          >
            {{ bodyLength() }} / {{ maxLength }}
          </p>
        }
        <vc-button type="submit" [disabled]="submitDisabled()" [loading]="threads.sending()">
          {{ threads.editingMessage() ? 'Salvar' : 'Responder' }}
        </vc-button>
      </form>
    </div>
  `,
  styles: `
    :host {
      display: block;
      min-height: 0;
      height: 100%;
      overflow: hidden;
    }
    .thread {
      display: grid;
      grid-template-rows: auto 1fr auto;
      height: 100%;
      min-height: 0;
      overflow: hidden;
    }
    .thread__header {
      display: flex;
      align-items: flex-start;
      justify-content: space-between;
      gap: 0.75rem;
      padding: var(--vc-space-4);
      border-bottom: 1px solid var(--vc-border);
    }
    .thread__header h2 {
      margin: 0;
      font-family: var(--vc-font-display);
      font-size: 1.05rem;
    }
    .thread__header p {
      margin: 0.2rem 0 0;
      color: var(--vc-ink-muted);
      font-size: 0.8rem;
    }
    .thread__scroll {
      min-height: 0;
      overflow: auto;
      overscroll-behavior: contain;
      padding: var(--vc-space-4);
      display: flex;
      flex-direction: column;
      gap: var(--vc-space-3);
    }
    .thread__parent {
      padding-bottom: var(--vc-space-3);
      border-bottom: 1px dashed var(--vc-border);
    }
    .thread__list,
    .thread__loading {
      display: flex;
      flex-direction: column;
      gap: var(--vc-space-3);
    }
    .thread__composer {
      display: grid;
      gap: 0.65rem;
      padding: var(--vc-space-4);
      border-top: 1px solid var(--vc-border);
    }
    .thread__reply {
      display: grid;
      grid-template-columns: 1fr auto;
      gap: 0.55rem;
      align-items: start;
      padding: 0.45rem 0.6rem;
      border-left: 3px solid var(--vc-brand);
      border-radius: 0 var(--vc-radius-sm) var(--vc-radius-sm) 0;
      background: color-mix(in srgb, var(--vc-brand) 8%, transparent);
    }
    .thread__reply-meta {
      display: grid;
      gap: 0.1rem;
      min-width: 0;
    }
    .thread__reply-meta strong {
      font-size: 0.8rem;
      color: var(--vc-brand);
    }
    .thread__reply-meta span {
      font-size: 0.78rem;
      color: var(--vc-ink-muted);
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .thread__reply .ghost {
      border: 0;
      background: transparent;
      color: var(--vc-brand);
      cursor: pointer;
      font: inherit;
      font-size: 1.1rem;
      line-height: 1;
      padding: 0;
    }
    .thread__counter {
      margin: 0;
      font-size: 0.75rem;
      color: var(--vc-ink-subtle);
      text-align: right;
    }
    .thread__counter--over {
      color: var(--vc-danger);
    }
  `,
})
export class ThreadPanel {
  readonly threads = inject(ThreadStore);
  readonly messages = inject(MessageStore);
  private readonly hub = inject(ChatHubService);
  private readonly drafts = inject(DraftStoreService);
  private readonly scroller = viewChild<ElementRef<HTMLElement>>('scroller');
  private readonly threadTextarea = viewChild<Textarea>('threadTextarea');
  readonly draft = signal('');
  readonly maxLength = MESSAGE_BODY_MAX_LENGTH;
  readonly bodyLength = computed(() => measureMessageBodyLength(this.draft()));
  readonly bodyTooLong = computed(() => isMessageBodyTooLong(this.draft()));
  readonly showCounter = computed(() => this.bodyLength() >= MESSAGE_BODY_COUNTER_THRESHOLD);
  readonly submitDisabled = computed(
    () =>
      !this.draft().trim() ||
      this.submitting() ||
      this.threads.sending() ||
      this.bodyTooLong(),
  );
  /** Sync gate so Enter×2 cannot start two sends before `threads.sending` flips. */
  private readonly submitting = signal(false);
  private lastTyping = 0;
  private boundConversationId: string | null = null;
  private restoringDraft = false;
  private draftBeforeEdit: { body: string } | null = null;

  constructor() {
    effect(() => {
      this.threads.sortedMessages();
      queueMicrotask(() => {
        const el = this.scroller()?.nativeElement;
        if (el) el.scrollTop = el.scrollHeight;
      });
    });

    effect(() => {
      const threadId = this.threads.active()?.id ?? null;
      untracked(() => {
        void this.onActiveThreadChanged(threadId);
      });
    });

    effect(() => {
      const editing = this.threads.editingMessage();
      if (!editing) return;
      untracked(() => {
        if (!this.draftBeforeEdit) {
          this.draftBeforeEdit = { body: this.draft() };
        }
        this.draft.set(editing.body);
      });
      queueMicrotask(() => this.threadTextarea()?.nativeElement()?.focus());
    });

    effect(() => {
      this.draft();
      if (this.restoringDraft || this.threads.editingMessage()) return;
      const conversationId = untracked(() => this.boundConversationId);
      if (!conversationId) return;
      this.persistDraftSoon();
    });
  }

  citePreview(body: string): string {
    return replyPreviewText(body);
  }

  onReply(message: ChatMessage): void {
    this.threads.setReplyTarget(message);
  }

  onStartEdit(message: ChatMessage): void {
    this.threads.startEdit(message);
  }

  cancelEdit(): void {
    this.threads.clearEdit();
    this.restoreDraftAfterEdit();
    queueMicrotask(() => this.threadTextarea()?.nativeElement()?.focus());
  }

  private restoreDraftAfterEdit(): void {
    const snapshot = this.draftBeforeEdit;
    this.draftBeforeEdit = null;
    this.restoringDraft = true;
    try {
      this.draft.set(snapshot?.body ?? '');
    } finally {
      this.restoringDraft = false;
    }
    if (this.boundConversationId) {
      this.persistDraftSoon();
    }
  }

  onQuoteClick(messageId: string): void {
    this.messages.jumpToMessage(messageId);
  }

  async onSubmit(event: Event): Promise<void> {
    event.preventDefault();
    if (this.submitting()) return;

    const editing = this.threads.editingMessage();
    if (editing) {
      const body = this.draft().trim();
      if (!body || isMessageBodyTooLong(body)) return;
      this.submitting.set(true);
      try {
        await this.threads.edit(editing.id, body);
        this.restoreDraftAfterEdit();
      } finally {
        this.submitting.set(false);
      }
      return;
    }

    const body = this.draft().trim();
    if (!body || isMessageBodyTooLong(body)) return;

    this.submitting.set(true);
    // Clear before await send so a second Enter cannot resubmit the same draft.
    this.draft.set('');
    const conversationId = this.boundConversationId;
    if (conversationId) {
      await this.drafts.remove(conversationId);
    }
    try {
      const ok = await this.threads.send(body);
      if (!ok) {
        this.draft.set(body);
        this.persistDraftSoon();
      }
    } finally {
      this.submitting.set(false);
    }
  }

  async onReact(messageId: string, emoji: string): Promise<void> {
    await this.threads.toggleReaction(messageId, emoji);
  }

  async onRemoveLinkPreview(messageId: string): Promise<void> {
    await this.messages.removeLinkPreview(messageId);
  }

  onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape' && this.threads.editingMessage()) {
      event.preventDefault();
      this.cancelEdit();
      return;
    }

    if (
      event.key === 'ArrowUp' &&
      !this.threads.editingMessage() &&
      !this.draft().trim()
    ) {
      const textarea = this.threadTextarea()?.nativeElement();
      const cursor = textarea?.selectionStart ?? 0;
      if (cursor === 0) {
        const last = this.threads.lastOwnPersistedMessage();
        if (last) {
          event.preventDefault();
          this.threads.startEdit(last);
          return;
        }
      }
    }

    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      void this.onSubmit(event);
      return;
    }

    const channelId = this.threads.active()?.channelId;
    if (!channelId) return;
    const now = Date.now();
    if (now - this.lastTyping > 1500) {
      this.lastTyping = now;
      void this.hub.sendTyping(channelId);
    }
  }

  private async onActiveThreadChanged(threadId: string | null): Promise<void> {
    const conversationId = threadId ? threadConversationId(threadId) : null;
    const previousId = this.boundConversationId;
    if (previousId === conversationId) return;

    if (previousId) {
      await this.persistDraftNow(previousId);
    }

    this.boundConversationId = conversationId;
    this.threads.clearReplyTarget();
    this.threads.clearEdit();
    this.draftBeforeEdit = null;

    if (!conversationId) {
      this.restoringDraft = true;
      this.draft.set('');
      this.restoringDraft = false;
      return;
    }

    await this.restoreDraft(conversationId);
  }

  private async restoreDraft(conversationId: string): Promise<void> {
    this.restoringDraft = true;
    try {
      const saved = await this.drafts.get(conversationId);
      this.draft.set(saved?.body ?? '');
      if (saved && (saved.selectionStart != null || saved.selectionEnd != null)) {
        queueMicrotask(() => {
          const textarea = this.threadTextarea()?.nativeElement();
          if (!textarea) return;
          const start = saved.selectionStart ?? saved.body.length;
          const end = saved.selectionEnd ?? start;
          updateTextareaSelection(textarea, saved.body, start, end);
        });
      }
    } finally {
      this.restoringDraft = false;
    }
  }

  private persistDraftSoon(): void {
    const conversationId = this.boundConversationId;
    if (!conversationId || this.restoringDraft) return;
    const textarea = this.threadTextarea()?.nativeElement();
    this.drafts.scheduleSave(conversationId, {
      body: this.draft(),
      selectionStart: textarea?.selectionStart,
      selectionEnd: textarea?.selectionEnd,
    });
  }

  private async persistDraftNow(conversationId: string): Promise<void> {
    const textarea = this.threadTextarea()?.nativeElement();
    await this.drafts.saveNow(conversationId, {
      body: this.draft(),
      selectionStart: textarea?.selectionStart,
      selectionEnd: textarea?.selectionEnd,
    });
  }
}
