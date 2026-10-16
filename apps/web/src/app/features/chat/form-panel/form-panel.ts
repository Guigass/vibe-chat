import { Component, inject, signal } from '@angular/core';
import { ui } from '../../../core/i18n/strings';
import { FormField, FormFieldType, FormSchema } from '../../../core/api/form-api.service';
import { FormStore } from '../../../core/services/form.store';
import { ChannelStore } from '../../../core/services/channel.store';
import { IconButton } from '../../../shared/ui/icon-button/icon-button';

@Component({
  selector: 'vc-form-panel',
  standalone: true,
  imports: [IconButton],
  template: `
    <section class="form-panel" [attr.aria-label]="ui.formPanel" data-testid="form-panel">
      <header>
        <h2>{{ ui.formPanel }}</h2>
        <vc-icon-button [label]="ui.closePanel" (click)="forms.closePanel()">
          <span aria-hidden="true">×</span>
        </vc-icon-button>
      </header>
      <p class="form-panel__hint">{{ ui.formVisibilityHint }}</p>
      @if (forms.saveError()) {
        <p role="alert">{{ forms.saveError() }}</p>
      }
      @if (editing()) {
        <form (submit)="save($event, true)">
          <label>{{ ui.formTitleLabel }}
            <input data-testid="form-title" [value]="title()" (input)="title.set(valueOf($event))" required />
          </label>
          @for (field of fields(); track $index) {
            <div class="form-panel__field">
              <label>{{ ui.formFieldLabel }}
                <input [attr.data-testid]="$index === 0 ? 'form-field-label' : null" [value]="field.label" (input)="patch($index, { label: valueOf($event) })" required />
              </label>
              <label>{{ ui.formFieldType }}
                <select [value]="field.type" (change)="patch($index, { type: typeOf($event) })">
                  <option value="text">{{ ui.formTypeText }}</option>
                  <option value="number">{{ ui.formTypeNumber }}</option>
                  <option value="choice">{{ ui.formTypeChoice }}</option>
                  <option value="date">{{ ui.formTypeDate }}</option>
                  <option value="user">{{ ui.formTypeUser }}</option>
                  <option value="attachment">{{ ui.formTypeAttachment }}</option>
                </select>
              </label>
              @if (field.type === 'choice') {
                <label>{{ ui.formOptionsLabel }}
                  <input [value]="field.options.join(', ')" (input)="patch($index, { options: split(valueOf($event)) })" />
                </label>
              }
              <label><input type="checkbox" [checked]="field.hidden" (change)="patch($index, { hidden: checkedOf($event), required: !checkedOf($event) })" /> {{ ui.formFieldHidden }}</label>
              <button type="button" (click)="remove($index)">{{ ui.formRemoveField }}</button>
            </div>
          }
          <button type="button" (click)="add()">{{ ui.formAddField }}</button>
          <fieldset>
            <legend>{{ ui.formReviewers }}</legend>
            @for (member of channels.members(); track member.userId) {
              <label>
                <input type="checkbox" [checked]="reviewers().includes(member.userId)" (change)="toggleReviewer(member.userId, checkedOf($event))" [attr.data-testid]="'form-reviewer'" />
                {{ member.displayName }}
              </label>
            }
          </fieldset>
          <label>{{ ui.formApprovalMode }}
            <select [value]="mode()" (change)="mode.set(modeOf($event))">
              <option value="single">{{ ui.formModeSingle }}</option>
              <option value="n_of_m">{{ ui.formModeQuorum }}</option>
            </select>
          </label>
          @if (mode() === 'n_of_m') {
            <label>{{ ui.formRequiredApprovals }}
              <input type="number" min="1" [value]="required()" (input)="required.set(numberOf($event))" />
            </label>
          }
          <label>{{ ui.formOnApproved }}
            <select [value]="onApproved()" (change)="onApproved.set(resultOf($event))">
              <option value="none">{{ ui.formOnNone }}</option>
              <option value="message">{{ ui.formOnMessage }}</option>
              <option value="task">{{ ui.formOnTask }}</option>
            </select>
          </label>
          <p class="form-panel__hint">{{ ui.formPreview }}: {{ preview() }}</p>
          <div class="form-panel__actions">
            <button type="button" (click)="save($event, false)" [disabled]="forms.saving()">{{ ui.formSaveDraft }}</button>
            <button type="submit" data-testid="form-publish" [disabled]="forms.saving()">{{ ui.formPublish }}</button>
          </div>
        </form>
      } @else if (forms.selected(); as form) {
        <button type="button" (click)="forms.back()">{{ ui.formBack }}</button>
        <h3>{{ form.title }}</h3>
        <p>{{ statusLabel(form.status) }}</p>
        @if (form.canSubmit && form.publishedSchema) {
          <form (submit)="submit($event)">
            @for (field of visible(form.publishedSchema); track field.key) {
              <label>{{ field.label }}
                @if (field.type === 'choice') {
                  <select [attr.data-testid]="'form-answer-' + field.key" (change)="answer(field.key, valueOf($event))">
                    <option value=""></option>
                    @for (option of field.options; track option) {
                      <option [value]="option">{{ option }}</option>
                    }
                  </select>
                } @else if (field.type === 'user') {
                  <select (change)="answer(field.key, valueOf($event))">
                    <option value=""></option>
                    @for (member of channels.members(); track member.userId) {
                      <option [value]="member.userId">{{ member.displayName }}</option>
                    }
                  </select>
                } @else {
                  <input [attr.data-testid]="'form-answer-' + field.key" [type]="inputType(field.type)" (input)="answer(field.key, valueOf($event))" />
                }
              </label>
            }
            <button type="submit" data-testid="form-submit" [disabled]="forms.saving()">{{ ui.formSubmit }}</button>
          </form>
        }
        <ul>
          @for (item of forms.submissions(); track item.id) {
            <li>
              <span>{{ stateLabel(item.state) }}</span>
              @if (item.answersPurged) {
                <span>{{ ui.formAnswersPurged }}</span>
              }
              @if (item.canDecide) {
                <button type="button" data-testid="form-approve" (click)="forms.decide(item.id, 'approve')">{{ ui.formApprove }}</button>
                <button type="button" (click)="forms.decide(item.id, 'reject')">{{ ui.formReject }}</button>
              }
              @if (item.decisions.length) {
                <ol [attr.aria-label]="ui.formDecisions">
                  @for (decision of item.decisions; track decision.version) {
                    <li>{{ nameOf(decision.actorUserId) }} · {{ decision.decision === 'approve' ? ui.formApprove : ui.formReject }}</li>
                  }
                </ol>
              }
            </li>
          }
        </ul>
        @if (form.canEdit) {
          <button type="button" (click)="forms.archive()">{{ ui.formArchive }}</button>
        }
      } @else {
        <button type="button" data-testid="form-create" (click)="start()">{{ ui.formCreate }}</button>
        @if (forms.loading()) {
          <p>{{ ui.loading }}</p>
        } @else if (forms.error()) {
          <p role="alert">{{ forms.error() }}</p>
        } @else if (!forms.items().length) {
          <p>{{ ui.formEmpty }}</p>
        } @else {
          <ul>
            @for (item of forms.items(); track item.id) {
              <li><button type="button" [attr.data-testid]="'form-open'" (click)="forms.open(item.id)">{{ item.title }} · {{ statusLabel(item.status) }}</button></li>
            }
          </ul>
        }
      }
    </section>
  `,
  styles: `
    .form-panel { display: grid; gap: var(--vc-space-3); padding: var(--vc-space-3); }
    .form-panel header, .form-panel__actions, .form-panel__field { display: flex; flex-wrap: wrap; gap: var(--vc-space-2); align-items: center; }
    .form-panel h2, .form-panel h3, .form-panel p { margin: 0; }
    .form-panel h2 { font-size: var(--vc-text-sm); }
    .form-panel__hint { color: var(--vc-ink-muted); font-size: var(--vc-text-sm); }
    .form-panel form, .form-panel ul, .form-panel li { display: grid; gap: var(--vc-space-2); }
    .form-panel ul, .form-panel ol { list-style: none; margin: 0; padding: 0; }
    .form-panel input, .form-panel select, .form-panel button {
      font: inherit; color: inherit; background: var(--vc-surface); border: 1px solid var(--vc-line); border-radius: var(--vc-radius-sm);
    }
    .form-panel label { display: grid; gap: 0.25rem; font-size: var(--vc-text-sm); }
  `,
})
export class FormPanel {
  readonly ui = ui;
  readonly forms = inject(FormStore);
  readonly channels = inject(ChannelStore);
  readonly editing = signal(false);
  readonly title = signal('');
  readonly fields = signal<FormField[]>([field()]);
  readonly reviewers = signal<string[]>([]);
  readonly mode = signal<'single' | 'n_of_m'>('single');
  readonly required = signal(1);
  readonly onApproved = signal<'none' | 'message' | 'task'>('none');
  private readonly answers = signal<Record<string, string | number>>({});

  start(): void {
    this.title.set('');
    this.fields.set([field()]);
    this.reviewers.set([]);
    this.mode.set('single');
    this.required.set(1);
    this.onApproved.set('none');
    this.editing.set(true);
  }

  preview(): string {
    return this.fields().filter((item) => !item.hidden).map((item) => item.label).filter(Boolean).join(', ');
  }

  add(): void {
    this.fields.update((items) => items.length >= 20 ? items : [...items, field()]);
  }

  remove(index: number): void {
    this.fields.update((items) => items.filter((_, item) => item !== index));
  }

  patch(index: number, change: Partial<FormField>): void {
    this.fields.update((items) => items.map((item, itemIndex) => itemIndex === index ? { ...item, ...change } : item));
  }

  toggleReviewer(userId: string, checked: boolean): void {
    this.reviewers.update((ids) => checked ? [...new Set([...ids, userId])] : ids.filter((id) => id !== userId));
  }

  async save(event: Event, publish: boolean): Promise<void> {
    event.preventDefault();
    const schema = this.schema();
    if (!schema) return;
    const saved = await this.forms.save(schema, this.title().trim(), publish);
    if (saved) this.editing.set(false);
  }

  async submit(event: Event): Promise<void> {
    event.preventDefault();
    await this.forms.submit(this.answers());
    this.answers.set({});
  }

  answer(key: string, value: string): void {
    this.answers.update((current) => ({ ...current, [key]: value }));
  }

  visible(schema: FormSchema): FormField[] {
    return schema.fields.filter((field) => !field.hidden);
  }

  statusLabel(status: string): string {
    if (status === 'published') return ui.formPublished;
    if (status === 'archived') return ui.formArchived;
    return ui.formDraft;
  }

  stateLabel(state: string): string {
    if (state === 'approved') return ui.formApproved;
    if (state === 'rejected') return ui.formRejected;
    return ui.formPending;
  }

  inputType(type: FormFieldType): string {
    if (type === 'number') return 'number';
    if (type === 'date') return 'date';
    return 'text';
  }

  nameOf(userId: string): string {
    return this.channels.members().find((member) => member.userId === userId)?.displayName ?? userId;
  }

  valueOf(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  checkedOf(event: Event): boolean {
    return (event.target as HTMLInputElement).checked;
  }

  typeOf(event: Event): FormFieldType {
    return (event.target as HTMLSelectElement).value as FormFieldType;
  }

  modeOf(event: Event): 'single' | 'n_of_m' {
    return (event.target as HTMLSelectElement).value as 'single' | 'n_of_m';
  }

  resultOf(event: Event): 'none' | 'message' | 'task' {
    return (event.target as HTMLSelectElement).value as 'none' | 'message' | 'task';
  }

  numberOf(event: Event): number {
    return Number((event.target as HTMLInputElement).value) || 1;
  }

  split(value: string): string[] {
    return value.split(',').map((item) => item.trim()).filter(Boolean);
  }

  private schema(): FormSchema | null {
    const fields = this.fields()
      .map((item) => ({ ...item, key: keyOf(item.label), label: item.label.trim() }))
      .filter((item) => item.key && item.label);
    if (!this.title().trim() || !fields.length || !this.reviewers().length) return null;
    return {
      contractVersion: 'form.schema.v1',
      fields,
      approval: {
        mode: this.mode(),
        required: this.mode() === 'single' ? 1 : this.required(),
        reviewerUserIds: this.reviewers(),
      },
      onApproved: this.onApproved(),
    };
  }
}

function field(): FormField {
  return { key: 'reason', type: 'text', label: '', required: true, hidden: false, options: [] };
}

function keyOf(label: string): string {
  const slug = label.normalize('NFD').replace(/\p{M}/gu, '').toLowerCase().replace(/[^a-z0-9]+/g, '_').replace(/^_|_$/g, '');
  return /^[a-z]/.test(slug) ? slug.slice(0, 40) : `f_${slug}`.slice(0, 40);
}
