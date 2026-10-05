import { Component, input, output } from '@angular/core';
import { ui } from '../../../core/i18n/strings';
import { ChatMessage } from '../../models/chat.models';
import { formatReactionAriaLabel } from './message-media';

@Component({
  selector: 'vc-message-reactions',
  standalone: true,
  templateUrl: './message-reactions.html',
  styleUrl: './message-reactions.scss',
})
export class MessageReactions {
  readonly message = input.required<ChatMessage>();
  readonly tooltips = input<Record<string, string>>({});
  readonly react = output<string>();
  readonly tooltip = output<string>();
  readonly ui = ui;

  reactionTooltip(emoji: string): string {
    return this.tooltips()[emoji] ?? '';
  }

  reactionAriaLabel(emoji: string): string {
    return formatReactionAriaLabel(emoji, this.reactionTooltip(emoji));
  }

  loadReactionTooltip(emoji: string): void {
    this.tooltip.emit(emoji);
  }
}
