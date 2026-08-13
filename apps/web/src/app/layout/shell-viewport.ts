/** Matches `.shell` overlay breakpoint in `shell.page.scss` (UX-003). */
export const SHELL_NARROW_BREAKPOINT_PX = 960;

export const SHELL_NARROW_MEDIA_QUERY = `(max-width: ${SHELL_NARROW_BREAKPOINT_PX}px)`;

/** B-184: icon-only desktop rail; distinct from global `data-density`. */
export const NAV_COMPACT_STORAGE_KEY = 'vc.navCompact';

export const NAV_COMPACT_WIDTH = '4.5rem';

/** Desktop rail open; narrow starts collapsed (overlay on demand). */
export function defaultSidebarOpen(narrow: boolean): boolean {
  return !narrow;
}

export function readNavCompact(): boolean {
  if (typeof localStorage === 'undefined') {
    return false;
  }
  return localStorage.getItem(NAV_COMPACT_STORAGE_KEY) === 'true';
}

export function writeNavCompact(compact: boolean): void {
  if (typeof localStorage === 'undefined') {
    return;
  }
  localStorage.setItem(NAV_COMPACT_STORAGE_KEY, compact ? 'true' : 'false');
}
