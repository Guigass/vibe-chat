export const DEFAULT_LOCALE = 'pt-BR';
export const SUPPORTED_LOCALES = [
  'pt-BR',
  'en',
  'es',
  'fr',
  'de',
  'it',
  'ja',
  'zh-CN',
  'ko',
  'ru',
] as const;
export type AppLocale = (typeof SUPPORTED_LOCALES)[number];

export const LOCALE_STORAGE_KEY = 'vc.locale';

export const LOCALE_OPTION_NAMES: Record<AppLocale, string> = {
  'pt-BR': 'Português',
  en: 'English',
  es: 'Español',
  fr: 'Français',
  de: 'Deutsch',
  it: 'Italiano',
  ja: '日本語',
  'zh-CN': '中文',
  ko: '한국어',
  ru: 'Русский',
};

const SUPPORTED_LOCALE_SET = new Set<string>(SUPPORTED_LOCALES);

const LANGUAGE_TO_LOCALE: Record<string, AppLocale> = {
  pt: 'pt-BR',
  en: 'en',
  es: 'es',
  fr: 'fr',
  de: 'de',
  it: 'it',
  ja: 'ja',
  zh: 'zh-CN',
  ko: 'ko',
  ru: 'ru',
};

export function isAppLocale(value: string | null | undefined): value is AppLocale {
  return value != null && SUPPORTED_LOCALE_SET.has(value);
}

export function detectBrowserLocale(
  languages: readonly string[] = typeof navigator === 'undefined'
    ? [DEFAULT_LOCALE]
    : (navigator.languages?.length ? navigator.languages : [navigator.language]),
): AppLocale {
  for (const raw of languages) {
    const tag = (raw ?? '').trim();
    if (!tag) {
      continue;
    }
    if (isAppLocale(tag)) {
      return tag;
    }
    const language = tag.split('-')[0]?.toLowerCase() ?? '';
    const mapped = LANGUAGE_TO_LOCALE[language];
    if (mapped) {
      return mapped;
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
