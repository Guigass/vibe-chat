import { definePreset, palette } from '@primeuix/themes';
import Aura from '@primeuix/themes/aura';

/**
 * PrimeNG preset mapped to VibeChat design tokens (teal / charcoal).
 * Dark mode follows `html[data-theme="dark"]` (ThemeService).
 */
export const VibeChatPreset = definePreset(Aura, {
  semantic: {
    primary: palette('#0d9488'),
    colorScheme: {
      light: {
        primary: {
          color: '{primary.500}',
          contrastColor: '#ffffff',
          hoverColor: '{primary.600}',
          activeColor: '{primary.700}',
        },
        highlight: {
          background: '{primary.50}',
          focusBackground: '{primary.100}',
          color: '{primary.700}',
          focusColor: '{primary.800}',
        },
        surface: {
          0: '#ffffff',
          50: '#f8fafc',
          100: '#f1f5f9',
          200: '#e7e5e4',
          300: '#d6d3d1',
          400: '#a8a29e',
          500: '#78716c',
          600: '#57534e',
          700: '#44403c',
          800: '#292524',
          900: '#1c1917',
          950: '#0c0a09',
        },
      },
      dark: {
        primary: {
          color: '{primary.400}',
          contrastColor: '#0c0f12',
          hoverColor: '{primary.300}',
          activeColor: '{primary.200}',
        },
        highlight: {
          background: 'color-mix(in srgb, {primary.400}, transparent 84%)',
          focusBackground: 'color-mix(in srgb, {primary.400}, transparent 76%)',
          color: 'rgba(245, 245, 244, 0.92)',
          focusColor: 'rgba(245, 245, 244, 0.92)',
        },
        surface: {
          0: '#161a1f',
          50: '#12151a',
          100: '#0c0f12',
          200: '#292524',
          300: '#44403c',
          400: '#57534e',
          500: '#78716c',
          600: '#a8a29e',
          700: '#d6d3d1',
          800: '#e7e5e4',
          900: '#f5f5f4',
          950: '#fafaf9',
        },
      },
    },
  },
});
