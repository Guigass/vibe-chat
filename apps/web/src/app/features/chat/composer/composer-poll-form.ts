import { Component, inject } from '@angular/core';
import { fillTemplate, ui } from '../../../core/i18n/strings';
import { Button, Input } from '../../../shared/ui';
import { ComposerPoll } from './composer-poll';

@Component({
  selector: 'vc-composer-poll-form',
  standalone: true,
  imports: [Button, Input],
  templateUrl: './composer-poll-form.html',
  styleUrl: './composer-poll-form.scss',
})
export class ComposerPollForm {
  private readonly polls = inject(ComposerPoll);
  readonly ui = ui;
  readonly fillTemplate = fillTemplate;
  readonly pollComposerOpen = this.polls.open;
  readonly pollQuestion = this.polls.question;
  readonly pollOptions = this.polls.options;
  readonly pollAllowMultiple = this.polls.allowMultiple;
  readonly pollAnonymous = this.polls.anonymous;
  readonly pollClosesAt = this.polls.closesAt;

  closePollComposer(): void {
    this.polls.close();
  }

  setPollOption(index: number, value: string): void {
    this.polls.setOption(index, value);
  }

  addPollOption(): void {
    this.polls.addOption();
  }

  submitPoll(event: Event): void {
    void this.polls.submit(event);
  }
}
