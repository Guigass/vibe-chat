import { Component, computed, inject, output, signal } from '@angular/core';
import { ui } from '../../../core/i18n/strings';
import { LocaleService } from '../../../core/i18n/locale.service';
import { IconButton } from '../../../shared/ui';
import { EmojiPicker } from '../../../shared/ui/emoji-picker/emoji-picker';
import { ComposerFormatKind } from './composer-format';
import { ComposerMentions } from './composer-mentions';
import { ComposerSlashSession } from './composer-slash-session';

@Component({
  selector: 'vc-composer-format-toolbar',
  standalone: true,
  imports: [IconButton, EmojiPicker],
  templateUrl: './composer-format-toolbar.html',
  styleUrl: './composer-format-toolbar.scss',
})
export class ComposerFormatToolbar {
  private readonly locales = inject(LocaleService);
  private readonly mentions = inject(ComposerMentions);
  private readonly slashSession = inject(ComposerSlashSession);
  readonly ui = ui;
  readonly emojiPickerOpen = signal(false);
  readonly emojiLocale = computed(() => (this.locales.locale() === 'en' ? 'en' : 'pt'));
  readonly format = output<ComposerFormatKind>();
  readonly emoji = output<string>();

  applyFormat(kind: ComposerFormatKind): void {
    this.format.emit(kind);
  }

  toggleEmojiPicker(event: Event): void {
    event.stopPropagation();
    this.emojiPickerOpen.update((open) => !open);
    this.mentions.close();
    this.slashSession.close();
  }

  insertEmoji(emoji: string): void {
    this.emojiPickerOpen.set(false);
    this.emoji.emit(emoji);
  }
}
