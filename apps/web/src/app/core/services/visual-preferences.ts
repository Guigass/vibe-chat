export const WALLPAPER_IDS = ['tide', 'mist', 'ember', 'slate'] as const;
export const ACCENT_IDS = ['ocean', 'amber', 'rose', 'slate'] as const;

export type WallpaperId = (typeof WALLPAPER_IDS)[number];
export type AccentId = (typeof ACCENT_IDS)[number];

export interface AccentPaletteEntry {
  id: AccentId;
  lightBubble: string;
  darkBubble: string;
}

/** Mirrors VisualPreferenceCatalog on the API. Unknown ids fall back to null. */
export const ACCENT_PALETTE: readonly AccentPaletteEntry[] = [
  { id: 'ocean', lightBubble: '#dbeafe', darkBubble: '#1e3a5f' },
  { id: 'amber', lightBubble: '#fef3c7', darkBubble: '#78350f' },
  { id: 'rose', lightBubble: '#ffe4e6', darkBubble: '#9f1239' },
  { id: 'slate', lightBubble: '#f5f5f4', darkBubble: '#44403c' },
];

export const LIGHT_INK = '#1c1917';
export const LIGHT_MUTED = '#57534e';
export const DARK_INK = '#f5f5f4';
export const DARK_MUTED = '#e7e5e4';
export const MIN_CONTRAST = 4.5;

export interface VisualChoice {
  wallpaper: WallpaperId | null;
  accent: AccentId | null;
}

export function resolveWallpaperId(id: string | null | undefined): WallpaperId | null {
  const trimmed = id?.trim();
  return trimmed && WALLPAPER_IDS.includes(trimmed as WallpaperId) ? (trimmed as WallpaperId) : null;
}

export function resolveAccentId(id: string | null | undefined): AccentId | null {
  const trimmed = id?.trim();
  return trimmed && ACCENT_IDS.includes(trimmed as AccentId) ? (trimmed as AccentId) : null;
}

export function applyVisualPreferences(
  wallpaperId: string | null | undefined,
  accentId: string | null | undefined,
): VisualChoice {
  const choice: VisualChoice = {
    wallpaper: resolveWallpaperId(wallpaperId),
    accent: resolveAccentId(accentId),
  };
  const root = document.documentElement;
  if (choice.wallpaper) {
    root.setAttribute('data-wallpaper', choice.wallpaper);
  } else {
    root.removeAttribute('data-wallpaper');
  }
  if (choice.accent) {
    root.setAttribute('data-accent', choice.accent);
  } else {
    root.removeAttribute('data-accent');
  }
  return choice;
}

export function contrastRatio(hexA: string, hexB: string): number {
  const lighter = Math.max(relativeLuminance(hexA), relativeLuminance(hexB));
  const darker = Math.min(relativeLuminance(hexA), relativeLuminance(hexB));
  return (lighter + 0.05) / (darker + 0.05);
}

function relativeLuminance(hex: string): number {
  const value = hex.replace('#', '');
  const channels = [0, 2, 4].map((index) => {
    const channel = Number.parseInt(value.slice(index, index + 2), 16) / 255;
    return channel <= 0.03928 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4;
  });
  return (0.2126 * channels[0]) + (0.7152 * channels[1]) + (0.0722 * channels[2]);
}
