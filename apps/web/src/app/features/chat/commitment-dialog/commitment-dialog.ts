import { A11yModule } from '@angular/cdk/a11y';
import { Component, effect, inject, signal } from '@angular/core';
import { ui } from '../../../core/i18n/strings';
import { CommitmentStore } from '../../../core/services/commitment.store';
import { ChannelStore } from '../../../core/services/channel.store';
import { CommitmentVisibility } from '../../../core/api/commitment-api.service';

@Component({
  selector: 'vc-commitment-dialog',
  standalone: true,
  imports: [A11yModule],
  template: `
    @if (commitments.draft(); as draft) {
      <div class="commitment-dialog__backdrop" (click)="commitments.cancelDraft()"></div>
      <section
        class="commitment-dialog"
        role="dialog"
        aria-modal="true"
        cdkTrapFocus
        [cdkTrapFocusAutoCapture]="true"
        [attr.aria-labelledby]="titleId"
        data-testid="commitment-dialog"
      >
        <header>
          <h2 [id]="titleId">
            {{ draft.kind === 'decision' ? ui.commitmentDialogDecision : ui.commitmentDialogAction }}
          </h2>
          <button type="button" (click)="commitments.cancelDraft()">{{ ui.closePanel }}</button>
        </header>
        <form (submit)="onSubmit($event)">
          <label>
            {{ ui.commitmentTitleLabel }}
            <input
              type="text"
              required
              maxlength="120"
              data-testid="commitment-title"
              [attr.aria-label]="ui.commitmentTitleLabel"
              [value]="title()"
              (input)="title.set(inputValue($event))"
            />
          </label>
          <label>
            {{ ui.commitmentDescriptionLabel }}
            <textarea
              maxlength="500"
              rows="3"
              [attr.aria-label]="ui.commitmentDescriptionLabel"
              [value]="description()"
              (input)="description.set(inputValue($event))"
            ></textarea>
          </label>
          <label>
            {{ ui.commitmentOwnerLabel }}
            <select
              [attr.aria-label]="ui.commitmentOwnerLabel"
              [value]="ownerUserId()"
              (change)="ownerUserId.set(inputValue($event))"
            >
              <option value="">{{ ui.commitmentNoOwner }}</option>
              @for (member of channels.members(); track member.userId) {
                <option [value]="member.userId">{{ member.displayName }}</option>
              }
            </select>
          </label>
          <label>
            {{ ui.commitmentDueLabel }}
            <input
              type="datetime-local"
              [attr.aria-label]="ui.commitmentDueLabel"
              [value]="dueAt()"
              (input)="dueAt.set(inputValue($event))"
            />
          </label>
          <label>
            {{ ui.commitmentVisibilityLabel }}
            <select
              [attr.aria-label]="ui.commitmentVisibilityLabel"
              [value]="visibility()"
              (change)="onVisibility($event)"
            >
              <option value="channel">{{ ui.commitmentVisibilityChannel }}</option>
              <option value="restricted">{{ ui.commitmentVisibilityRestricted }}</option>
            </select>
          </label>
          @if (commitments.saveError()) {
            <p role="alert">{{ commitments.saveError() }}</p>
          }
          <div class="commitment-dialog__actions">
            <button type="button" (click)="commitments.cancelDraft()">{{ ui.closePanel }}</button>
            <button type="submit" [disabled]="commitments.saving()">{{ ui.commitmentCreate }}</button>
          </div>
        </form>
      </section>
    }
  `,
  styles: `
    .commitment-dialog__backdrop {
      position: fixed;
      inset: 0;
      background: color-mix(in srgb, var(--vc-ink) 35%, transparent);
      z-index: 30;
    }
    .commitment-dialog {
      position: fixed;
      z-index: 31;
      top: 12vh;
      left: 50%;
      transform: translateX(-50%);
      width: min(28rem, calc(100vw - 2rem));
      display: grid;
      gap: var(--vc-space-3);
      padding: var(--vc-space-4);
      border-radius: var(--vc-radius-md);
      background: var(--vc-surface-elevated);
      color: var(--vc-ink);
      box-shadow: var(--vc-shadow-overlay, 0 12px 40px color-mix(in srgb, var(--vc-ink) 18%, transparent));
    }
    .commitment-dialog header,
    .commitment-dialog__actions {
      display: flex;
      justify-content: space-between;
      align-items: center;
      gap: var(--vc-space-2);
    }
    .commitment-dialog h2 { margin: 0; font-size: var(--vc-text-sm); }
    .commitment-dialog form { display: grid; gap: var(--vc-space-3); }
    .commitment-dialog label { display: grid; gap: 0.25rem; font-size: var(--vc-text-sm); }
    .commitment-dialog input,
    .commitment-dialog textarea,
    .commitment-dialog select {
      font: inherit;
      color: var(--vc-ink);
      background: var(--vc-surface);
      border: 1px solid var(--vc-border);
      border-radius: var(--vc-radius-sm);
      padding: 0.4rem 0.55rem;
    }
    .commitment-dialog button {
      font: inherit;
      cursor: pointer;
      border: 1px solid var(--vc-border);
      border-radius: var(--vc-radius-sm);
      background: transparent;
      color: var(--vc-ink);
      padding: 0.35rem 0.7rem;
    }
    .commitment-dialog button[type='submit'] {
      background: var(--vc-brand);
      color: var(--vc-on-brand, #fff);
      border-color: transparent;
    }
  `,
})
export class CommitmentDialog {
  readonly ui = ui;
  readonly titleId = 'commitment-dialog-title';
  readonly commitments = inject(CommitmentStore);
  readonly channels = inject(ChannelStore);
  readonly title = signal('');
  readonly description = signal('');
  readonly ownerUserId = signal('');
  readonly dueAt = signal('');
  readonly visibility = signal<CommitmentVisibility>('channel');

  constructor() {
    effect(() => {
      if (!this.commitments.draft()) return;
      this.title.set('');
      this.description.set('');
      this.ownerUserId.set('');
      this.dueAt.set('');
      this.visibility.set('channel');
    });
  }

  inputValue(event: Event): string {
    return (event.target as HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement).value;
  }

  onVisibility(event: Event): void {
    const value = this.inputValue(event);
    this.visibility.set(value === 'restricted' ? 'restricted' : 'channel');
  }

  async onSubmit(event: Event): Promise<void> {
    event.preventDefault();
    if (!this.title().trim()) return;
    await this.commitments.submit({
      title: this.title(),
      description: this.description(),
      ownerUserId: this.ownerUserId(),
      dueAt: this.dueAt(),
      visibility: this.visibility(),
    });
  }
}
