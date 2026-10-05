import { Injectable, inject, signal } from '@angular/core';
import { ui } from '../../../core/i18n/strings';
import { MessageStore } from '../../../core/services/message.store';
import { ComposerState } from './composer-state';

export function pollDraftError(question: string, options: string[], closesAt: string): string | null {
  if (question.length < 1 || question.length > 500 || options.length < 2 || options.length > 10) {
    return ui.composerPollNeedOptions;
  }
  if (options.some((option) => option.length > 100)) {
    return ui.composerPollOptionTooLong;
  }
  const closesLocal = closesAt.trim();
  const parsed = closesLocal ? new Date(closesLocal).toISOString() : null;
  if (parsed && Number.isNaN(Date.parse(parsed))) {
    return ui.composerPollDeadlineInvalid;
  }
  return null;
}

@Injectable()
export class ComposerPoll {
  private readonly messages = inject(MessageStore);
  private readonly state = inject(ComposerState);

  readonly open = signal(false);
  readonly question = signal('');
  readonly options = signal<string[]>(['', '']);
  readonly allowMultiple = signal(false);
  readonly anonymous = signal(false);
  readonly closesAt = signal('');

  openWith(question = '', options?: string[]): void {
    this.question.set(question);
    this.options.set(options && options.length >= 2 ? options : ['', '']);
    this.allowMultiple.set(false);
    this.anonymous.set(false);
    this.closesAt.set('');
    this.open.set(true);
  }

  close(): void {
    this.open.set(false);
    this.question.set('');
    this.options.set(['', '']);
    this.closesAt.set('');
  }

  setOption(index: number, value: string): void {
    this.options.update((items) => items.map((item, i) => (i === index ? value : item)));
  }

  addOption(): void {
    this.options.update((items) => (items.length >= 10 ? items : [...items, '']));
  }

  async submit(event: Event): Promise<void> {
    event.preventDefault();
    const question = this.question().trim();
    const options = this.options().map((item) => item.trim()).filter(Boolean);
    const error = pollDraftError(question, options, this.closesAt());
    if (error) {
      this.state.validationError.set(error);
      return;
    }
    const closesLocal = this.closesAt().trim();
    const closesAt = closesLocal ? new Date(closesLocal).toISOString() : null;
    const ok = await this.messages.createPoll({
      question,
      options,
      allowMultiple: this.allowMultiple(),
      anonymous: this.anonymous(),
      closesAt,
    });
    if (!ok) {
      this.state.validationError.set(ui.composerPollCreateError);
      return;
    }
    this.state.validationError.set(null);
    this.close();
  }
}
