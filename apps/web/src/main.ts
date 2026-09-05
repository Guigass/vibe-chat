import { loadTranslations } from '@angular/localize';
import { catalogUrl, DEFAULT_LOCALE, resolveBootstrapLocale } from './app/core/i18n/locale';

async function bootstrap(): Promise<void> {
  const locale = resolveBootstrapLocale();
  document.documentElement.lang = locale;
  if (locale !== DEFAULT_LOCALE) {
    try {
      const response = await fetch(catalogUrl(locale));
      if (response.ok) {
        const catalog = (await response.json()) as { translations?: Record<string, string> };
        if (catalog.translations) {
          loadTranslations(catalog.translations);
        }
      }
    } catch {
      // Missing catalog → keep source pt-BR strings (never show raw ids).
    }
  }

  const { bootstrapApplication } = await import('@angular/platform-browser');
  const { appConfig } = await import('./app/app.config');
  const { App } = await import('./app/app');
  await bootstrapApplication(App, appConfig);
}

bootstrap().catch((err) => console.error(err));
