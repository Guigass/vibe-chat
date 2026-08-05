/** Matches `.shell` overlay breakpoint in `shell.page.scss` (UX-003). */
export const SHELL_NARROW_BREAKPOINT_PX = 960;

export const SHELL_NARROW_MEDIA_QUERY = `(max-width: ${SHELL_NARROW_BREAKPOINT_PX}px)`;

/** Desktop rail open; narrow starts collapsed (overlay on demand). */
export function defaultSidebarOpen(narrow: boolean): boolean {
  return !narrow;
}
