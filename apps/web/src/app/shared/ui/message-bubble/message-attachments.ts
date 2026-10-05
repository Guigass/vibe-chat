import { Component, input, output } from '@angular/core';
import { ChatMessage, MessageAttachment } from '../../models/chat.models';
import { AttachmentPreview } from '../attachment-preview/attachment-preview';

@Component({
  selector: 'vc-message-attachments',
  standalone: true,
  imports: [AttachmentPreview],
  templateUrl: './message-attachments.html',
  styleUrl: './message-attachments.scss',
})
export class MessageAttachments {
  readonly message = input.required<ChatMessage>();
  readonly previewUrls = input<Record<string, string>>({});
  readonly downloadUrls = input<Record<string, string>>({});
  readonly transcript = input<string | null>(null);
  readonly transcribeEnabled = input(false);
  readonly imageOpen = output<string>();
  readonly fileOpen = output<MessageAttachment>();
  readonly transcribeRequest = output<MessageAttachment>();

  openLightbox(id: string): void {
    this.imageOpen.emit(id);
  }

  download(attachment: MessageAttachment): void {
    this.fileOpen.emit(attachment);
  }

  transcribe(attachment: MessageAttachment): void {
    this.transcribeRequest.emit(attachment);
  }
}
