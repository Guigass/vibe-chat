import { Injectable, inject, signal } from '@angular/core';
import { ApiService } from '../api/api.service';
import { AuthService } from '../auth/auth.service';
import { ui } from '../i18n/strings';
import {
  type AccentId,
  type VisualChoice,
  type WallpaperId,
  applyVisualPreferences,
  resolveAccentId,
  resolveWallpaperId,
} from './visual-preferences';

@Injectable({ providedIn: 'root' })
export class VisualPreferenceService {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);

  readonly saved = signal<VisualChoice>({ wallpaper: null, accent: null });
  readonly draft = signal<VisualChoice>({ wallpaper: null, accent: null });
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);
  readonly savedFlash = signal(false);

  private userId: string | null = null;

  async load(): Promise<void> {
    if (!this.canPersist()) {
      this.commitLocal(null, null);
      return;
    }

    try {
      const me = await this.api.getMe();
      this.userId = me.userId;
      this.commitLocal(resolveWallpaperId(me.chatWallpaperId), resolveAccentId(me.accentColorId));
    } catch {
      this.commitLocal(null, null);
    }
  }

  previewWallpaper(id: string | null): void {
    this.draft.update((current) => ({ ...current, wallpaper: resolveWallpaperId(id) }));
    this.paint(this.draft());
    this.savedFlash.set(false);
  }

  previewAccent(id: string | null): void {
    this.draft.update((current) => ({ ...current, accent: resolveAccentId(id) }));
    this.paint(this.draft());
    this.savedFlash.set(false);
  }

  async save(): Promise<void> {
    await this.persist(this.draft());
  }

  async reset(): Promise<void> {
    const cleared: VisualChoice = { wallpaper: null, accent: null };
    this.draft.set(cleared);
    this.paint(cleared);
    await this.persist(cleared);
  }

  revertPreview(): void {
    const saved = this.saved();
    this.draft.set(saved);
    this.paint(saved);
  }

  private commitLocal(wallpaper: WallpaperId | null, accent: AccentId | null): void {
    const choice: VisualChoice = { wallpaper, accent };
    this.saved.set(choice);
    this.draft.set(choice);
    this.paint(choice);
  }

  private paint(choice: VisualChoice): void {
    applyVisualPreferences(choice.wallpaper, choice.accent);
  }

  private async persist(choice: VisualChoice): Promise<void> {
    this.error.set(null);
    if (!this.canPersist() || !this.userId) {
      this.saved.set(choice);
      return;
    }

    this.saving.set(true);
    try {
      const result = await this.api.updateAppearance(this.userId, {
        chatWallpaperId: choice.wallpaper,
        accentColorId: choice.accent,
      });
      this.commitLocal(resolveWallpaperId(result.chatWallpaperId), resolveAccentId(result.accentColorId));
      this.savedFlash.set(true);
    } catch {
      this.error.set(ui.appearanceError);
    } finally {
      this.saving.set(false);
    }
  }

  private canPersist(): boolean {
    return this.auth.isAuthenticated() && !this.auth.isOfflineDemo();
  }
}
