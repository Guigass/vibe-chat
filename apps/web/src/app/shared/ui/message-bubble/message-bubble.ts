import { DatePipe } from '@angular/common';
import { Component, input } from '@angular/core';
import { ChatMessage } from '../../models/chat.models';
import { Avatar } from '../avatar/avatar';

@Component({
  selector: 'vc-message-bubble',
  standalone: true,
  imports: [Avatar, DatePipe],
  template: `
    <article
      class="vc-msg vc-anim-fade-in"
      [class.vc-msg--mine]="message().mine"
      [attr.data-status]="message().status"
    >
      @if (!message().mine) {
        <vc-avatar [name]="message().authorName" [size]="34" />
      }
      <div class="vc-msg__body">
        <header>
          <strong>{{ message().authorName }}</strong>
          <time [attr.datetime]="message().createdAt">{{ message().createdAt | date: 'shortTime' }}</time>
          @if (message().status === 'sending') {
            <span class="vc-msg__status">enviando…</span>
          } @else if (message().status === 'sent') {
            <span class="vc-msg__status">enviada</span>
          } @else if (message().status === 'failed') {
            <span class="vc-msg__status vc-msg__status--fail">falhou</span>
          } @else if (message().status === 'persisted') {
            <span class="vc-msg__status">salva</span>
          }
        </header>
        <p>{{ message().body }}</p>
      </div>
    </article>
  `,
  styles: `
    .vc-msg {
      display: flex;
      gap: 0.7rem;
      align-items: flex-start;
      max-width: min(720px, 100%);
    }
    .vc-msg--mine {
      margin-left: auto;
      flex-direction: row-reverse;
    }
    .vc-msg__body {
      padding: 0.65rem 0.85rem;
      border-radius: var(--vc-radius-md);
      background: var(--vc-msg-theirs);
      border: 1px solid var(--vc-border);
    }
    .vc-msg--mine .vc-msg__body {
      background: var(--vc-msg-mine);
      border-color: color-mix(in srgb, var(--vc-brand) 28%, var(--vc-border));
    }
    header {
      display: flex;
      flex-wrap: wrap;
      gap: 0.45rem;
      align-items: baseline;
      margin-bottom: 0.25rem;
    }
    strong {
      font-size: 0.88rem;
      font-family: var(--vc-font-display);
    }
    time,
    .vc-msg__status {
      font-size: 0.72rem;
      color: var(--vc-ink-subtle);
    }
    .vc-msg__status--fail {
      color: var(--vc-danger);
    }
    p {
      margin: 0;
      white-space: pre-wrap;
      line-height: 1.45;
      word-break: break-word;
    }
  `,
})
export class MessageBubble {
  readonly message = input.required<ChatMessage>();
}
