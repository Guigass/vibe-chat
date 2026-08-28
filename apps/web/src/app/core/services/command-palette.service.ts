import { Injectable, signal } from '@angular/core';
import { readRecentPaletteIds, writeRecentPaletteId } from '../../shared/command-palette/palette-recent';

@Injectable({ providedIn: 'root' })
export class CommandPaletteService {
  private readonly paletteOpenSignal = signal(false);
  private readonly sheetOpenSignal = signal(false);
  private readonly composerFocusTickSignal = signal(0);
  private readonly recentIdsSignal = signal<string[]>([]);
  private previousFocus: HTMLElement | null = null;

  readonly paletteOpen = this.paletteOpenSignal.asReadonly();
  readonly sheetOpen = this.sheetOpenSignal.asReadonly();
  readonly composerFocusTick = this.composerFocusTickSignal.asReadonly();
  readonly recentIds = this.recentIdsSignal.asReadonly();

  hydrateRecents(userId: string | undefined | null): void {
    this.recentIdsSignal.set(userId ? readRecentPaletteIds(userId) : []);
  }

  rememberRecent(userId: string | undefined | null, id: string): void {
    if (!userId) return;
    this.recentIdsSignal.set(writeRecentPaletteId(userId, id));
  }

  openPalette(): void {
    this.captureFocus();
    this.sheetOpenSignal.set(false);
    this.paletteOpenSignal.set(true);
  }

  closePalette(options?: { restoreFocus?: boolean }): void {
    const restore = options?.restoreFocus !== false;
    this.paletteOpenSignal.set(false);
    if (restore) this.restoreFocus();
    else this.previousFocus = null;
  }

  openShortcutSheet(): void {
    if (!this.paletteOpenSignal()) {
      this.captureFocus();
    }
    this.sheetOpenSignal.set(true);
  }

  closeShortcutSheet(options?: { restoreFocus?: boolean }): void {
    const restore = options?.restoreFocus !== false && !this.paletteOpenSignal();
    this.sheetOpenSignal.set(false);
    if (restore) this.restoreFocus();
  }

  requestComposerFocus(): void {
    this.composerFocusTickSignal.update((tick) => tick + 1);
  }

  anyOverlayOpen(): boolean {
    return this.paletteOpenSignal() || this.sheetOpenSignal();
  }

  private captureFocus(): void {
    if (this.paletteOpenSignal() || this.sheetOpenSignal()) return;
    const active = document.activeElement;
    this.previousFocus = active instanceof HTMLElement ? active : null;
  }

  private restoreFocus(): void {
    const el = this.previousFocus;
    this.previousFocus = null;
    queueMicrotask(() => el?.focus?.());
  }
}
