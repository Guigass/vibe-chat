import { Component, inject } from '@angular/core';
import { ui } from '../../../core/i18n/strings';
import { MessageStore } from '../../../core/services/message.store';
import { ComposerSchedule } from './composer-schedule';

@Component({
  selector: 'vc-composer-schedule-fields',
  standalone: true,
  templateUrl: './composer-schedule-fields.html',
  styleUrl: './composer-schedule-fields.scss',
})
export class ComposerScheduleFields {
  private readonly scheduler = inject(ComposerSchedule);
  readonly messages = inject(MessageStore);
  readonly ui = ui;
  readonly scheduleMode = this.scheduler.mode;
  readonly scheduleAt = this.scheduler.at;
  readonly scheduleZone = this.scheduler.zone;
  readonly scheduleHint = this.scheduler.hint;

  inputValue(event: Event): string {
    return this.scheduler.inputValue(event);
  }
}
