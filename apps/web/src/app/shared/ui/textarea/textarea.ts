import { Component, ElementRef, input, model, output, viewChild } from '@angular/core';

@Component({
  selector: 'vc-textarea',
  standalone: true,
  template: `
    <label class="vc-field">
      @if (label()) {
        <span class="vc-field__label">{{ label() }}</span>
      }
      <textarea
        #control
        class="vc-field__control"
        rows="1"
        [attr.placeholder]="placeholder()"
        [attr.aria-label]="label() || placeholder()"
        [disabled]="disabled()"
        [value]="value()"
        (input)="onInput($event)"
        (keydown)="keydown.emit($event)"
      ></textarea>
    </label>
  `,
  styles: `
    .vc-field {
      display: grid;
      gap: 0.35rem;
      width: 100%;
    }
    .vc-field__label {
      font-size: 0.85rem;
      color: var(--vc-ink-muted);
      font-weight: 500;
    }
    .vc-field__control {
      width: 100%;
      min-height: 2.75rem;
      max-height: 10rem;
      resize: none;
      padding: 0.7rem 0.85rem;
      border-radius: var(--vc-radius-md);
      border: 1px solid var(--vc-border);
      background: var(--vc-composer-bg);
      color: var(--vc-ink);
      line-height: 1.45;
    }
  `,
})
export class Textarea {
  private readonly controlRef = viewChild<ElementRef<HTMLTextAreaElement>>('control');

  readonly value = model('');
  readonly label = input('');
  readonly placeholder = input('');
  readonly disabled = input(false);
  readonly keydown = output<KeyboardEvent>();

  nativeElement(): HTMLTextAreaElement | null {
    return this.controlRef()?.nativeElement ?? null;
  }

  onInput(event: Event): void {
    const el = event.target as HTMLTextAreaElement;
    this.value.set(el.value);
    el.style.height = 'auto';
    el.style.height = `${Math.min(el.scrollHeight, 160)}px`;
  }
}
