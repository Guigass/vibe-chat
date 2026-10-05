import { Component, inject } from '@angular/core';
import { fillTemplate, ui } from '../../../core/i18n/strings';
import { AttachmentQueueService } from './attachment-queue.service';
import {
  attachmentIconKind,
  formatFileSize,
  formatVideoDuration,
  resolveContentType,
} from './attachment-upload';
import type { AttachmentIconKind, PendingAttachment } from './attachment-upload';

@Component({
  selector: 'vc-composer-attachment-list',
  standalone: true,
  templateUrl: './composer-attachment-list.html',
  styleUrl: './composer-attachment-list.scss',
})
export class ComposerAttachmentList {
  readonly attachments = inject(AttachmentQueueService);
  readonly ui = ui;
  readonly fillTemplate = fillTemplate;
  readonly formatVideoDuration = formatVideoDuration;

  iconKindFor(file: File): AttachmentIconKind {
    return attachmentIconKind(resolveContentType(file));
  }

  sizeFor(item: PendingAttachment): string {
    return formatFileSize(item.restoredSizeBytes ?? item.file.size);
  }
}
