import '@angular/compiler';
import { DestroyRef, Injector } from '@angular/core';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ThemeService } from './theme.service';

const THEME_KEY = 'vc.theme';
const DENSITY_KEY = 'vc.density';

type StorageMap = Record<string, string>;

function installDomMocks(initialStorage: StorageMap = {}): {
  storage: StorageMap;
  root: { attributes: StorageMap; style: { colorScheme: string }; dataset: StorageMap };
} {
  const storage: StorageMap = { ...initialStorage };
  const attributes: StorageMap = {};
  const style = { colorScheme: '' };
  const root = {
    attributes,
    style,
    dataset: attributes,
    setAttribute(name: string, value: string) {
      attributes[name.startsWith('data-') ? name.slice(5) : name] = value;
      attributes[name] = value;
    },
    removeAttribute(name: string) {
      delete attributes[name];
      if (name.startsWith('data-')) {
        delete attributes[name.slice(5)];
      }
    },
  };

  // dataset.theme reads data-theme; mirror common jsdom behavior for tests.
  Object.defineProperty(root, 'dataset', {
    get() {
      return {
        get theme() {
          return attributes['data-theme'] ?? attributes.theme;
        },
        get density() {
          return attributes['data-density'] ?? attributes.density;
        },
      };
    },
  });

  vi.stubGlobal('localStorage', {
    getItem: (key: string) => storage[key] ?? null,
    setItem: (key: string, value: string) => {
      storage[key] = value;
    },
    removeItem: (key: string) => {
      delete storage[key];
    },
    clear: () => {
      for (const key of Object.keys(storage)) delete storage[key];
    },
  });

  vi.stubGlobal('document', {
    documentElement: root,
  });

  vi.stubGlobal(
    'window',
    {
      matchMedia: vi.fn().mockImplementation((query: string) => ({
        matches: false,
        media: query,
        onchange: null,
        addListener: vi.fn(),
        removeListener: vi.fn(),
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
        dispatchEvent: vi.fn(),
      })),
    },
  );

  return { storage, root: root as never };
}

describe('ThemeService (BUG-007)', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  function createService(): ThemeService {
    const injector = Injector.create({
      providers: [
        ThemeService,
        {
          provide: DestroyRef,
          useValue: { onDestroy: vi.fn() },
        },
      ],
    });
    return injector.get(ThemeService);
  }

  it('applies data-theme synchronously on construct from storage', () => {
    const { root } = installDomMocks({ [THEME_KEY]: 'dark' });
    const service = createService();

    expect(service.theme()).toBe('dark');
    expect(root.dataset.theme).toBe('dark');
    expect(root.style.colorScheme).toBe('dark');
  });

  it('setTheme pins dark mode on the document and in localStorage', () => {
    const { storage, root } = installDomMocks();
    const service = createService();
    service.setTheme('dark');

    expect(service.theme()).toBe('dark');
    expect(root.dataset.theme).toBe('dark');
    expect(storage[THEME_KEY]).toBe('dark');
  });

  it('toggleTheme switches light and dark and persists the pin', () => {
    const { storage, root } = installDomMocks();
    const service = createService();
    expect(service.theme()).toBe('light');

    service.toggleTheme();
    expect(service.theme()).toBe('dark');
    expect(root.dataset.theme).toBe('dark');
    expect(storage[THEME_KEY]).toBe('dark');

    service.toggleTheme();
    expect(service.theme()).toBe('light');
    expect(root.dataset.theme).toBe('light');
    expect(storage[THEME_KEY]).toBe('light');
  });

  it('does not leave density unset on construct', () => {
    const { root, storage } = installDomMocks();
    createService();
    expect(root.dataset.density).toBe('comfortable');
    expect(storage[DENSITY_KEY]).toBe('comfortable');
  });
});
