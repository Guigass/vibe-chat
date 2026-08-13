import { Directive } from '@angular/core';
import {
  BrnTooltip,
  provideBrnTooltipDefaultOptions,
  provideBrnTooltipGroup,
} from '@spartan-ng/brain/tooltip';

/** App-wide defaults for rail / icon tooltips (tokens --vc-*). */
export function provideVcTooltipDefaults() {
  return provideBrnTooltipDefaultOptions({
    showDelay: 200,
    hideDelay: 80,
    position: 'right',
    tooltipContentClasses: 'vc-tooltip',
    arrowClasses: () => 'vc-tooltip__arrow',
    svgClasses: 'vc-tooltip__svg',
  });
}

/** Skip delay when moving between sibling tooltips (e.g. compact rail). */
export function provideVcTooltipGroup() {
  return provideBrnTooltipGroup({ skipDelayDuration: 300 });
}

/**
 * Thin alias over BrnTooltip so callers use `[vcTooltip]="…"` with VibeChat defaults.
 * Keep `aria-label` on the host; do not set native `title` when this is active.
 */
@Directive({
  selector: '[vcTooltip]',
  standalone: true,
  hostDirectives: [
    {
      directive: BrnTooltip,
      inputs: [
        'brnTooltip: vcTooltip',
        'position',
        'tooltipDisabled',
        'showDelay',
        'hideDelay',
      ],
    },
  ],
})
export class VcTooltip {}
