export const DEFAULT_LOCALE = 'pt-BR';
export const SUPPORTED_LOCALES = ['pt-BR', 'en'] as const;
export type AppLocale = (typeof SUPPORTED_LOCALES)[number];

export const LOCALE_STORAGE_KEY = 'vc.locale';

export const LOCALE_OPTION_NAMES: Record<AppLocale, string> = {
  'pt-BR': 'Português',
  en: 'English',
};

export function isAppLocale(value: string | null | undefined): value is AppLocale {
  return value === 'pt-BR' || value === 'en';
}

export function detectBrowserLocale(
  languages: readonly string[] = typeof navigator === 'undefined'
    ? [DEFAULT_LOCALE]
    : (navigator.languages?.length ? navigator.languages : [navigator.language]),
): AppLocale {
  for (const raw of languages) {
    const tag = (raw ?? '').toLowerCase();
    if (tag === 'en' || tag.startsWith('en-')) {
      return 'en';
    }
    if (tag === 'pt' || tag.startsWith('pt-')) {
      return 'pt-BR';
    }
  }
  return DEFAULT_LOCALE;
}

export function readStoredLocale(
  storage: Pick<Storage, 'getItem'> | null = typeof localStorage === 'undefined' ? null : localStorage,
): AppLocale | null {
  if (!storage) {
    return null;
  }
  const raw = storage.getItem(LOCALE_STORAGE_KEY);
  return isAppLocale(raw) ? raw : null;
}

export function persistLocale(
  locale: AppLocale,
  storage: Pick<Storage, 'setItem'> | null = typeof localStorage === 'undefined' ? null : localStorage,
): void {
  storage?.setItem(LOCALE_STORAGE_KEY, locale);
}

export function resolveBootstrapLocale(
  storage: Pick<Storage, 'getItem'> | null = typeof localStorage === 'undefined' ? null : localStorage,
  languages?: readonly string[],
): AppLocale {
  return readStoredLocale(storage) ?? detectBrowserLocale(languages);
}

export function catalogUrl(locale: AppLocale): string {
  return `/locale/messages.${locale}.json`;
}
