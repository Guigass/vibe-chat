import { Component, OnDestroy, inject } from '@angular/core';
import { ui } from '../../../core/i18n/strings';
import { ACCENT_PALETTE } from '../../../core/services/visual-preferences';
import { VisualPreferenceService } from '../../../core/services/visual-preference.service';
import { Button } from '../../../shared/ui';

interface ChoiceOption {
  id: string | null;
  label: string;
  swatch: string;
}

@Component({
  selector: 'vc-appearance-panel',
  standalone: true,
  imports: [Button],
  template: `
    <fieldset class="appearance" data-testid="appearance-panel">
      <legend>{{ ui.appearance }}</legend>

      <p class="appearance__label" id="appearance-wallpaper">{{ ui.appearanceWallpaper }}</p>
      <p class="appearance__hint">{{ ui.appearanceWallpaperHint }}</p>
      <div class="appearance__choices" role="radiogroup" aria-labelledby="appearance-wallpaper">
        @for (option of wallpapers; track option.label) {
          <button
            type="button"
            class="appearance__choice"
            role="radio"
            [attr.aria-checked]="prefs.draft().wallpaper === option.id"
            [class.is-active]="prefs.draft().wallpaper === option.id"
            (click)="prefs.previewWallpaper(option.id)"
          >
            <span class="appearance__swatch" [style.background-color]="option.swatch" aria-hidden="true"></span>
            {{ option.label }}
          </button>
        }
      </div>

      <p class="appearance__label" id="appearance-accent">{{ ui.appearanceAccent }}</p>
      <p class="appearance__hint">{{ ui.appearanceAccentHint }}</p>
      <div class="appearance__choices" role="radiogroup" aria-labelledby="appearance-accent">
        @for (option of accents; track option.label) {
          <button
            type="button"
            class="appearance__choice"
            role="radio"
            [attr.aria-checked]="prefs.draft().accent === option.id"
            [class.is-active]="prefs.draft().accent === option.id"
            (click)="prefs.previewAccent(option.id)"
          >
            <span class="appearance__swatch" [style.background-color]="option.swatch" aria-hidden="true"></span>
            {{ option.label }}
          </button>
        }
      </div>

      <div class="appearance__preview" data-testid="appearance-preview" [attr.aria-label]="ui.appearancePreview">
        <span class="appearance__bubble">{{ ui.appearancePreview }}</span>
      </div>

      @if (prefs.error()) {
        <p class="appearance__error" role="alert">{{ prefs.error() }}</p>
      }

      <div class="appearance__actions">
        <vc-button type="button" variant="ghost" [disabled]="prefs.saving()" (click)="prefs.reset()">
          {{ ui.appearanceReset }}
        </vc-button>
        <vc-button type="button" variant="primary" [loading]="prefs.saving()" (click)="prefs.save()">
          {{ prefs.saving() ? ui.appearanceSaving : ui.appearanceSave }}
        </vc-button>
        @if (prefs.savedFlash()) {
          <span class="appearance__saved" role="status">{{ ui.appearanceSaved }}</span>
        }
      </div>
    </fieldset>
  `,
  styles: `
    .appearance {
      display: flex;
      flex-direction: column;
      gap: var(--vc-space-2);
      border: 1px solid var(--vc-border);
      border-radius: var(--vc-radius-md);
      padding: var(--vc-space-3);
      margin: 0;
      background: var(--vc-surface);
    }
    .appearance legend,
    .appearance__label {
      padding: 0 var(--vc-space-1);
      font-size: 0.8rem;
      font-weight: 600;
      color: var(--vc-ink-muted);
    }
    .appearance__label {
      margin: var(--vc-space-2) 0 0;
      padding: 0;
    }
    .appearance__hint,
    .appearance__error {
      margin: 0;
      color: var(--vc-ink-muted);
      font-size: 0.8rem;
    }
    .appearance__error {
      color: var(--vc-danger);
    }
    .appearance__choices {
      display: flex;
      flex-wrap: wrap;
      gap: var(--vc-space-1);
    }
    .appearance__choice {
      display: inline-flex;
      align-items: center;
      gap: 0.35rem;
      min-height: 2rem;
      padding: 0.2rem 0.55rem;
      border: 1px solid var(--vc-border);
      border-radius: var(--vc-radius-sm);
      background: var(--vc-surface-elevated);
      color: var(--vc-ink);
      font: inherit;
      font-size: 0.8rem;
      cursor: pointer;
    }
    .appearance__choice.is-active {
      border-color: var(--vc-brand);
      background: var(--vc-brand-soft);
      color: var(--vc-brand-ink);
      font-weight: 600;
    }
    .appearance__choice:focus-visible {
      outline: none;
      box-shadow: var(--vc-focus-ring);
    }
    .appearance__swatch {
      width: 0.85rem;
      height: 0.85rem;
      border-radius: var(--vc-radius-sm);
      border: 1px solid var(--vc-border);
    }
    .appearance__preview {
      min-height: 4.5rem;
      display: flex;
      align-items: center;
      justify-content: flex-end;
      padding: 0.75rem;
      border: 1px solid var(--vc-border);
      border-radius: var(--vc-radius-md);
      background-color: var(--vc-surface);
      background-image:
        linear-gradient(color-mix(in srgb, var(--vc-surface) 28%, transparent), color-mix(in srgb, var(--vc-surface) 28%, transparent)),
        var(--vc-chat-wallpaper, none);
      background-size: cover;
      background-position: center;
    }
    .appearance__bubble {
      max-width: 70%;
      padding: 0.45rem 0.7rem;
      border-radius: var(--vc-radius-md);
      background: var(--vc-msg-mine);
      color: var(--vc-ink);
      font-size: 0.8rem;
    }
    .appearance__actions {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--vc-space-2);
    }
    .appearance__saved {
      color: var(--vc-success);
      font-size: 0.8rem;
    }
  `,
})
export class AppearancePanel implements OnDestroy {
  readonly prefs = inject(VisualPreferenceService);
  readonly ui = ui;
  readonly wallpapers: ChoiceOption[] = [
    { id: null, label: ui.appearanceDefault, swatch: 'var(--vc-surface)' },
    { id: 'tide', label: ui.wallpaperTide, swatch: '#5eead4' },
    { id: 'mist', label: ui.wallpaperMist, swatch: '#cbd5e1' },
    { id: 'ember', label: ui.wallpaperEmber, swatch: '#fdba74' },
    { id: 'slate', label: ui.wallpaperSlate, swatch: '#a8a29e' },
  ];
  readonly accents: ChoiceOption[] = [
    { id: null, label: ui.appearanceDefault, swatch: 'var(--vc-brand)' },
    ...ACCENT_PALETTE.map((entry) => ({
      id: entry.id,
      label: accentLabel(entry.id),
      swatch: entry.lightBubble,
    })),
  ];

  ngOnDestroy(): void {
    if (!this.prefs.saving()) {
      this.prefs.revertPreview();
    }
  }
}

function accentLabel(id: string): string {
  switch (id) {
    case 'ocean':
      return ui.accentOcean;
    case 'amber':
      return ui.accentAmber;
    case 'rose':
      return ui.accentRose;
    case 'slate':
      return ui.accentSlate;
    default:
      return ui.appearanceDefault;
  }
}
