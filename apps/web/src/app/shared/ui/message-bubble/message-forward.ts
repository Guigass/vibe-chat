import { DatePipe } from '@angular/common';
import { Component, input } from '@angular/core';
import { ui } from '../../../core/i18n/strings';
import { ChatMessage, MessageForwardedFrom } from '../../models/chat.models';
import { formatForwardOrigin as formatForwardOriginText } from './message-media';

@Component({
  selector: 'vc-message-forward',
  standalone: true,
  imports: [DatePipe],
  templateUrl: './message-forward.html',
  styleUrl: './message-forward.scss',
})
export class MessageForward {
  readonly message = input.required<ChatMessage>();
  readonly ui = ui;
  formatForwardOrigin(origin: MessageForwardedFrom): string {
    return formatForwardOriginText(origin);
  }
}
