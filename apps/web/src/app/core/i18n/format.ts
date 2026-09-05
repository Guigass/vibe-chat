import { DEFAULT_LOCALE, type AppLocale, resolveBootstrapLocale } from './locale';

export function activeLocale(): AppLocale {
  return resolveBootstrapLocale();
}

export function formatLocaleDate(
  value: string | Date,
  locale: AppLocale = activeLocale(),
  options?: Intl.DateTimeFormatOptions,
): string {
  const date = value instanceof Date ? value : new Date(value);
  if (Number.isNaN(date.getTime())) {
    return '';
  }
  return new Intl.DateTimeFormat(locale, options).format(date);
}

export function formatLocaleNumber(value: number, locale: AppLocale = activeLocale()): string {
  return new Intl.NumberFormat(locale).format(value);
}

/** ICU-style 0 / 1 / N labels. Source strings are pt-BR; $localize swaps en. */
export function pluralCount(count: number, kind: 'result' | 'pin'): string {
  if (kind === 'pin') {
    if (count === 1) {
      return $localize`:@@plural.pin.one:1 fixada`;
    }
    return $localize`:@@plural.pin.other:${count}:count: fixadas`;
  }
  if (count === 0) {
    return $localize`:@@plural.result.zero:Nenhum resultado`;
  }
  if (count === 1) {
    return $localize`:@@plural.result.one:1 resultado`;
  }
  return $localize`:@@plural.result.other:${count}:count: resultados`;
}

export function dayRelativeLabel(which: 'today' | 'yesterday', locale: AppLocale = DEFAULT_LOCALE): string {
  if (which === 'today') {
    return locale === 'en' ? 'Today' : $localize`:@@day.today:Hoje`;
  }
  return locale === 'en' ? 'Yesterday' : $localize`:@@day.yesterday:Ontem`;
}
