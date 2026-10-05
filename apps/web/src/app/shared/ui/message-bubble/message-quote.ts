import { Component, input, output } from '@angular/core';
import { ui } from '../../../core/i18n/strings';
import { ChatMessage } from '../../models/chat.models';

@Component({
  selector: 'vc-message-quote',
  standalone: true,
  templateUrl: './message-quote.html',
  styleUrl: './message-quote.scss',
})
export class MessageQuote {
  readonly message = input.required<ChatMessage>();
  readonly quoteClick = output<string>();
  readonly ui = ui;
}
