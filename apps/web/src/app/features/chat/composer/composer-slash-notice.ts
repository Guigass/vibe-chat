import { Component, inject } from '@angular/core';
import { ui } from '../../../core/i18n/strings';
import { SlashCommandsService } from './slash-commands.service';

@Component({
  selector: 'vc-composer-slash-notice',
  standalone: true,
  templateUrl: './composer-slash-notice.html',
  styleUrl: './composer-slash-notice.scss',
})
export class ComposerSlashNotice {
  readonly slash = inject(SlashCommandsService);
  readonly ui = ui;
}
