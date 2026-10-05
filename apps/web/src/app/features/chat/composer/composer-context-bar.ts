import { Component, inject, output } from '@angular/core';
import { ui } from '../../../core/i18n/strings';
import { replyPreviewText } from '../../../core/services/message-sync';
import { MessageStore } from '../../../core/services/message.store';

@Component({
  selector: 'vc-composer-context-bar',
  standalone: true,
  templateUrl: './composer-context-bar.html',
  styleUrl: './composer-context-bar.scss',
})
export class ComposerContextBar {
  readonly messages = inject(MessageStore);
  readonly ui = ui;
  readonly cancelEdit = output<void>();

  citePreview(body: string): string {
    return replyPreviewText(body);
  }

  requestCancelEdit(): void {
    this.cancelEdit.emit();
  }
}
