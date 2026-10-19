import { Component, inject, input, output, signal } from '@angular/core';
import { FilesApiService } from '../../../core/api/files-api.service';
import { ui } from '../../../core/i18n/strings';
import { attachmentScanHold, type AttachmentScanHold } from '../../attachments/attachment-preview';
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
  readonly own = input(false);
  readonly imageOpen = output<string>();
  readonly fileOpen = output<MessageAttachment>();
  readonly transcribeRequest = output<MessageAttachment>();
  readonly ui = ui;
  readonly retrying = signal(false);
  private readonly overrides = signal<Record<string, MessageAttachment>>({});
  private readonly files = inject(FilesApiService);

  openLightbox(id: string): void {
    this.imageOpen.emit(id);
  }

  download(attachment: MessageAttachment): void {
    this.fileOpen.emit(attachment);
  }

  transcribe(attachment: MessageAttachment): void {
    this.transcribeRequest.emit(attachment);
  }

  shown(attachment: MessageAttachment): MessageAttachment {
    return this.overrides()[attachment.id] ?? attachment;
  }

  hold(attachment: MessageAttachment): AttachmentScanHold | null {
    return attachmentScanHold(attachment);
  }

  scanLabel(hold: AttachmentScanHold): string {
    if (hold === 'blocked') return ui.attachScanBlocked;
    if (hold === 'unavailable') return ui.attachScanUnavailable;
    return ui.attachScanVerifying;
  }

  canRetry(attachment: MessageAttachment): boolean {
    return this.own()
      && (attachment.scanStatus ?? '').toLowerCase() === 'scanfailed'
      && (attachment.status ?? '').toLowerCase() !== 'ready';
  }

  async retry(attachment: MessageAttachment): Promise<void> {
    const channelId = this.message().channelId;
    if (!channelId || this.retrying()) return;
    this.retrying.set(true);
    try {
      const updated = await this.files.rescanAttachment(channelId, attachment.id);
      this.overrides.update((current) => ({ ...current, [attachment.id]: { ...attachment, ...updated } }));
    } finally {
      this.retrying.set(false);
    }
  }
}
