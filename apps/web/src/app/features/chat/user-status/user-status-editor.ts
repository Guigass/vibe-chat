import { A11yModule } from '@angular/cdk/a11y';
import { Component, effect, inject, output, signal } from '@angular/core';
import { translateErrorCode, ui } from '../../../core/i18n/strings';
import { UserStatusStore } from '../../../core/services/user-status.store';
import { UserStatusStateName } from '../../../shared/status/user-status';

const STATES: UserStatusStateName[] = ['focus', 'meeting', 'vacation', 'custom'];

@Component({
  selector: 'vc-user-status-editor',
  standalone: true,
  imports: [A11yModule],
  template: `
    <div class="status-editor__backdrop" (click)="closed.emit()"></div>
    <section
      class="status-editor"
      role="dialog"
      aria-modal="true"
      cdkTrapFocus
      [cdkTrapFocusAutoCapture]="true"
      [attr.aria-labelledby]="titleId"
      data-testid="user-status-editor"
    >
      <header class="status-editor__head">
        <h2 [id]="titleId">{{ ui.statusEditorTitle }}</h2>
        <button type="button" class="status-editor__close" (click)="closed.emit()">{{ ui.closePanel }}</button>
      </header>
      <p class="status-editor__lead">{{ ui.statusEditorLead }}</p>

      <div class="status-editor__states" role="radiogroup" [attr.aria-label]="ui.statusState">
        @for (state of states; track state) {
          <button
            type="button"
            role="radio"
            [attr.aria-checked]="selected() === state"
            [class.is-active]="selected() === state"
            [attr.data-testid]="'user-status-state-' + state"
            (click)="selected.set(state)"
          >
            {{ stateLabel(state) }}
          </button>
        }
      </div>

      <label class="status-editor__field">
        <span>{{ ui.statusEmoji }}</span>
        <input
          [value]="emoji()"
          maxlength="16"
          autocomplete="off"
          data-testid="user-status-emoji"
          (input)="emoji.set(readValue($event))"
        />
      </label>

      <label class="status-editor__field">
        <span>{{ ui.statusText }}</span>
        <input
          [value]="text()"
          maxlength="80"
          autocomplete="off"
          data-testid="user-status-text"
          (input)="text.set(readValue($event))"
        />
      </label>

      <label class="status-editor__check">
        <input
          type="checkbox"
          data-testid="user-status-end-of-day"
          [checked]="clearAtEndOfDay()"
          (change)="onEndOfDay($event)"
        />
        <span>{{ ui.statusClearEndOfDay }}</span>
      </label>

      <fieldset class="status-editor__expires">
        <legend>{{ ui.statusExpires }}</legend>
        @for (option of expiryOptions; track option) {
          <label>
            <input
              type="radio"
              name="status-expiry"
              [checked]="expiry() === option"
              [disabled]="clearAtEndOfDay() && option !== 'none'"
              (change)="expiry.set(option)"
            />
            <span>{{ expiryLabel(option) }}</span>
          </label>
        }
      </fieldset>

      <p class="status-editor__preview" data-testid="user-status-preview">
        <span class="status-editor__preview-label">{{ ui.statusPreview }}</span>
        <span class="status-editor__preview-line">{{ preview() }}</span>
      </p>

      @if (store.error(); as code) {
        <p class="status-editor__error" role="alert">{{ errorText(code) }}</p>
      }
      @if (store.loading()) {
        <p role="status">{{ ui.statusLoading }}</p>
      }

      <div class="status-editor__actions">
        <button type="button" data-testid="user-status-save" [disabled]="store.loading()" (click)="save()">
          {{ ui.statusSave }}
        </button>
        <button type="button" data-testid="user-status-clear" [disabled]="store.loading()" (click)="askClear()">
          {{ confirmingClear() ? ui.statusConfirmClear : ui.statusClear }}
        </button>
        <button type="button" (click)="closed.emit()">{{ ui.statusCancel }}</button>
      </div>
    </section>
  `,
  styles: `
    :host {
      position: fixed;
      inset: 0;
      z-index: 40;
      display: grid;
      place-items: end center;
      padding: var(--vc-space-4);
    }
    .status-editor__backdrop {
      position: absolute;
      inset: 0;
      border: 0;
      background: color-mix(in srgb, var(--vc-ink) 35%, transparent);
    }
    .status-editor {
      position: relative;
      width: min(28rem, 100%);
      max-height: min(90dvh, 40rem);
      overflow: auto;
      display: grid;
      gap: 0.75rem;
      padding: var(--vc-space-4);
      border: 1px solid var(--vc-border);
      border-radius: var(--vc-radius-md);
      background: var(--vc-surface-elevated);
      color: var(--vc-ink);
    }
    .status-editor__head {
      display: flex;
      justify-content: space-between;
      align-items: center;
      gap: 0.5rem;
    }
    .status-editor h2 {
      margin: 0;
      font-family: var(--vc-font-display);
      font-size: 1.15rem;
    }
    .status-editor__close,
    .status-editor__actions button,
    .status-editor__states button {
      border: 1px solid var(--vc-border);
      background: transparent;
      color: var(--vc-ink);
      border-radius: var(--vc-radius-sm);
      min-height: 2.25rem;
      padding: 0.35rem 0.7rem;
      font: inherit;
      cursor: pointer;
    }
    .status-editor__lead,
    .status-editor__preview {
      margin: 0;
      color: var(--vc-ink-muted);
    }
    .status-editor__states {
      display: flex;
      flex-wrap: wrap;
      gap: 0.4rem;
    }
    .status-editor__states button.is-active {
      border-color: var(--vc-brand);
      color: var(--vc-brand-ink);
      background: color-mix(in srgb, var(--vc-brand) 16%, transparent);
    }
    .status-editor__field {
      display: grid;
      gap: 0.25rem;
    }
    .status-editor__field input {
      min-height: 2.5rem;
      border: 1px solid var(--vc-border);
      border-radius: var(--vc-radius-sm);
      background: var(--vc-surface);
      color: var(--vc-ink);
      padding: 0 0.6rem;
      font: inherit;
    }
    .status-editor__check,
    .status-editor__expires label {
      display: flex;
      align-items: center;
      gap: 0.45rem;
    }
    .status-editor__expires {
      border: 0;
      margin: 0;
      padding: 0;
      display: grid;
      gap: 0.35rem;
    }
    .status-editor__preview-line {
      display: block;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
      color: var(--vc-ink);
      font-weight: 600;
    }
    .status-editor__error {
      margin: 0;
      color: var(--vc-danger, #b42318);
    }
    .status-editor__actions {
      display: flex;
      flex-wrap: wrap;
      gap: 0.4rem;
    }
  `,
})
export class UserStatusEditor {
  readonly ui = ui;
  readonly titleId = 'user-status-editor-title';
  readonly states = STATES;
  readonly expiryOptions = ['none', '1h', '4h'] as const;
  readonly store = inject(UserStatusStore);
  readonly closed = output<void>();

  readonly selected = signal<UserStatusStateName>('focus');
  readonly emoji = signal('');
  readonly text = signal('');
  readonly clearAtEndOfDay = signal(false);
  readonly expiry = signal<(typeof this.expiryOptions)[number]>('none');
  readonly confirmingClear = signal(false);

  constructor() {
    let copied = false;
    effect(() => {
      const mine = this.store.mine();
      if (copied || !mine) return;
      copied = true;
      const status = mine.status;
      if (!status) return;
      this.selected.set(status.state);
      this.emoji.set(status.emoji);
      this.text.set(status.text);
      this.clearAtEndOfDay.set(status.clearAtEndOfDay);
    });
  }

  preview(): string {
    return `${this.emoji().trim()} ${this.text().trim()}`.trim() || this.stateLabel(this.selected());
  }

  stateLabel(state: UserStatusStateName): string {
    switch (state) {
      case 'focus':
        return ui.statusFocus;
      case 'meeting':
        return ui.statusMeeting;
      case 'vacation':
        return ui.statusVacation;
      default:
        return ui.statusCustom;
    }
  }

  expiryLabel(option: (typeof this.expiryOptions)[number]): string {
    switch (option) {
      case '1h':
        return ui.statusExpires1h;
      case '4h':
        return ui.statusExpires4h;
      default:
        return ui.statusExpiresNone;
    }
  }

  errorText(code: string): string {
    if (code === 'load' || code === 'save') return ui.statusErrorSave;
    const translated = translateErrorCode(code);
    return translated === code ? ui.statusErrorSave : translated;
  }

  readValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  onEndOfDay(event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    this.clearAtEndOfDay.set(checked);
    if (checked) this.expiry.set('none');
  }

  async save(): Promise<void> {
    this.confirmingClear.set(false);
    const ok = await this.store.save({
      state: this.selected(),
      emoji: this.emoji().trim(),
      text: this.text().trim(),
      clearAtEndOfDay: this.clearAtEndOfDay(),
      expiresAt: this.expiresAt(),
    });
    if (ok) this.closed.emit();
  }

  async askClear(): Promise<void> {
    if (!this.confirmingClear()) {
      this.confirmingClear.set(true);
      return;
    }
    const ok = await this.store.clearMine();
    if (ok) this.closed.emit();
  }

  private expiresAt(): string | null {
    if (this.clearAtEndOfDay() || this.expiry() === 'none') return null;
    const hours = this.expiry() === '1h' ? 1 : 4;
    return new Date(Date.now() + hours * 60 * 60 * 1000).toISOString();
  }
}
