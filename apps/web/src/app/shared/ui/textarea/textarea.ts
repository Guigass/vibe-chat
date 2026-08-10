import {
  Component,
  effect,
  ElementRef,
  input,
  model,
  output,
  viewChild,
} from '@angular/core';

const TEXTAREA_MAX_HEIGHT_PX = 192; // ~12rem

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
      max-height: 12rem;
      resize: none;
      overflow-y: auto;
      padding: 0.7rem 0.85rem;
      border-radius: var(--vc-radius-md);
      border: 1px solid var(--vc-border);
      background: var(--vc-composer-bg);
      color: var(--vc-ink);
      line-height: 1.45;
      transition: height var(--vc-dur-fast) var(--vc-ease-out);
      scrollbar-width: thin;
      scrollbar-color: var(--vc-ink-subtle) transparent;
    }
    .vc-field__control::-webkit-scrollbar {
      width: 0.4rem;
    }
    .vc-field__control::-webkit-scrollbar-track {
      background: transparent;
    }
    .vc-field__control::-webkit-scrollbar-thumb {
      background: var(--vc-ink-subtle);
      border-radius: var(--vc-radius-sm);
    }
    .vc-field__control:focus,
    .vc-field__control:focus-visible {
      outline: none;
      border-color: color-mix(in srgb, var(--vc-brand) 45%, var(--vc-border));
      box-shadow: 0 0 0 1px color-mix(in srgb, var(--vc-brand) 28%, transparent);
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

  constructor() {
    effect(() => {
      this.value();
      queueMicrotask(() => this.autosize(this.nativeElement()));
    });
  }

  nativeElement(): HTMLTextAreaElement | null {
    return this.controlRef()?.nativeElement ?? null;
  }

  onInput(event: Event): void {
    const el = event.target as HTMLTextAreaElement;
    this.value.set(el.value);
    this.autosize(el);
  }

  private autosize(el: HTMLTextAreaElement | null): void {
    if (!el) return;
    el.style.height = 'auto';
    el.style.height = `${Math.min(el.scrollHeight, TEXTAREA_MAX_HEIGHT_PX)}px`;
  }
}
