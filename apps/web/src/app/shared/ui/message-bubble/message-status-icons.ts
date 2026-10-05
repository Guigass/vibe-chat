import { Component, input } from '@angular/core';
import { ui } from '../../../core/i18n/strings';
import { ChatMessage } from '../../models/chat.models';

@Component({
  selector: 'vc-message-status-icons',
  standalone: true,
  templateUrl: './message-status-icons.html',
  styleUrl: './message-status-icons.scss',
})
export class MessageStatusIcons {
  readonly message = input.required<ChatMessage>();
  readonly showDelivery = input(false);
  readonly ui = ui;
}
