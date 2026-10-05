import { Component, input, output } from '@angular/core';
import { ui } from '../../../core/i18n/strings';
import { MessageLinkPreview as LinkPreview } from '../../models/chat.models';
import { formatLinkPreviewLabel } from './message-media';

@Component({
  selector: 'vc-message-link-preview',
  standalone: true,
  templateUrl: './message-link-preview.html',
  styleUrl: './message-link-preview.scss',
})
export class MessageLinkPreviewCard {
  readonly visibleLinkPreview = input<LinkPreview | null>(null);
  readonly linkPreviewImageUrl = input<string | null>(null);
  readonly imageError = output<void>();
  readonly ui = ui;

  linkPreviewLabel(preview: LinkPreview): string {
    return formatLinkPreviewLabel(preview);
  }

  onLinkPreviewImageError(): void {
    this.imageError.emit();
  }
}
