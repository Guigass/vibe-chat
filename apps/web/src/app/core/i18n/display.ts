import { ui } from './strings';

/** Seed space names stay in the DB; the sidebar label follows the UI locale. */
export function displaySpaceName(name: string | null | undefined): string {
  const trimmed = name?.trim() ?? '';
  if (!trimmed) {
    return ui.navOthers;
  }
  switch (trimmed.toLowerCase()) {
    case 'geral':
      return ui.navSpaceGeral;
    case 'engenharia':
      return ui.navSpaceEngineering;
    default:
      return trimmed;
  }
}
