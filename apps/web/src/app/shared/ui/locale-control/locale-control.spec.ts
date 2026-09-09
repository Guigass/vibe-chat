import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it, vi } from 'vitest';
import { LocaleService } from '../../../core/i18n/locale.service';
import type { AppLocale } from '../../../core/i18n/locale';
import { LocaleControl } from './locale-control';

describe('LocaleControl', () => {
  it('shows the active locale on a late-created instance (settings panel)', async () => {
    const locale = signal<AppLocale>('en');
    await TestBed.configureTestingModule({
      imports: [LocaleControl],
      providers: [
        {
          provide: LocaleService,
          useValue: {
            locale,
            apply: vi.fn(async () => undefined),
          },
        },
      ],
    }).compileComponents();

    const first = TestBed.createComponent(LocaleControl);
    first.detectChanges();
    await first.whenStable();
    expect(nativeSelect(first.nativeElement).value).toBe('en');

    const second = TestBed.createComponent(LocaleControl);
    second.detectChanges();
    await second.whenStable();
    expect(nativeSelect(second.nativeElement).value).toBe('en');
  });

  it('applies the chosen locale from the field variant', async () => {
    const locale = signal<AppLocale>('pt-BR');
    const apply = vi.fn(async (next: AppLocale) => locale.set(next));
    await TestBed.configureTestingModule({
      imports: [LocaleControl],
      providers: [{ provide: LocaleService, useValue: { locale, apply } }],
    }).compileComponents();

    const fixture = TestBed.createComponent(LocaleControl);
    fixture.componentRef.setInput('variant', 'field');
    fixture.detectChanges();
    await fixture.whenStable();

    const select = nativeSelect(fixture.nativeElement);
    expect(select.value).toBe('pt-BR');
    select.value = 'en';
    select.dispatchEvent(new Event('change'));
    expect(apply).toHaveBeenCalledWith('en');
  });
});

function nativeSelect(host: HTMLElement): HTMLSelectElement {
  const el = host.querySelector('[data-testid="locale-select"]');
  if (!(el instanceof HTMLSelectElement)) {
    throw new Error('locale-select missing');
  }
  return el;
}
