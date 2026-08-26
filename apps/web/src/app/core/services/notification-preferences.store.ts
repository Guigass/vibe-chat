import { Injectable, OnDestroy, computed, inject, signal } from '@angular/core';
import { ApiService } from '../api/api.service';
import {
  ChannelMuteDuration,
  ChannelNotificationOverride,
  NotificationLevel,
  NotificationPreferences,
} from '../../shared/models/chat.models';

const WEEKDAY_ORDER = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];

/** Client-side mirror of PushDispatchPolicies.IsWithinDnd — best-effort UI only, server enforces it. */
function isWithinDndWindow(
  start: string,
  end: string,
  daysMask: number,
  timeZone: string,
  now: Date,
): boolean {
  let parts: Intl.DateTimeFormatPart[];
  try {
    parts = new Intl.DateTimeFormat('en-US', {
      timeZone,
      hour: '2-digit',
      minute: '2-digit',
      hour12: false,
      weekday: 'short',
    }).formatToParts(now);
  } catch {
    return false;
  }

  const hour = Number(parts.find((p) => p.type === 'hour')?.value ?? '0') % 24;
  const minute = Number(parts.find((p) => p.type === 'minute')?.value ?? '0');
  const weekday = parts.find((p) => p.type === 'weekday')?.value ?? '';
  const dayIndex = WEEKDAY_ORDER.indexOf(weekday);
  if (daysMask !== 0 && dayIndex >= 0 && (daysMask & (1 << dayIndex)) === 0) {
    return false;
  }

  const nowMinutes = hour * 60 + minute;
  const [startH, startM] = start.split(':').map(Number);
  const [endH, endM] = end.split(':').map(Number);
  const startMinutes = startH * 60 + startM;
  const endMinutes = endH * 60 + endM;
  return startMinutes <= endMinutes
    ? nowMinutes >= startMinutes && nowMinutes < endMinutes
    : nowMinutes >= startMinutes || nowMinutes < endMinutes;
}

@Injectable({ providedIn: 'root' })
export class NotificationPreferencesStore implements OnDestroy {
  private readonly api = inject(ApiService);

  private readonly preferencesSignal = signal<NotificationPreferences | null>(null);
  private readonly loadingSignal = signal(false);
  private readonly errorSignal = signal<string | null>(null);
  private readonly panelOpenSignal = signal(false);
  private readonly nowTickSignal = signal(Date.now());
  private readonly tickHandle = setInterval(() => this.nowTickSignal.set(Date.now()), 60_000);

  readonly preferences = this.preferencesSignal.asReadonly();
  readonly loading = this.loadingSignal.asReadonly();
  readonly error = this.errorSignal.asReadonly();
  readonly panelOpen = this.panelOpenSignal.asReadonly();

  readonly channelOverrides = computed(() => {
    const map = new Map<string, ChannelNotificationOverride>();
    for (const item of this.preferencesSignal()?.channelOverrides ?? []) {
      map.set(item.channelId, item);
    }
    return map;
  });

  readonly mutedChannelIds = computed(() => {
    this.nowTickSignal();
    const ids = new Set<string>();
    for (const [channelId, override] of this.channelOverrides()) {
      if (override.level === 'None' && (!override.mutedUntil || new Date(override.mutedUntil).getTime() > Date.now())) {
        ids.add(channelId);
      }
    }
    return ids;
  });

  readonly channelsWithOverride = computed(() => new Set(this.channelOverrides().keys()));

  readonly dndActive = computed(() => {
    this.nowTickSignal();
    const prefs = this.preferencesSignal();
    if (!prefs?.dndEnabled || !prefs.dndStart || !prefs.dndEnd || !prefs.timeZone) {
      return false;
    }
    return isWithinDndWindow(prefs.dndStart, prefs.dndEnd, prefs.dndDays, prefs.timeZone, new Date());
  });

  ngOnDestroy(): void {
    clearInterval(this.tickHandle);
  }

  isMuted(channelId: string): boolean {
    return this.mutedChannelIds().has(channelId);
  }

  openPanel(): void {
    this.panelOpenSignal.set(true);
    void this.load();
  }

  closePanel(): void {
    this.panelOpenSignal.set(false);
  }

  togglePanel(): void {
    if (this.panelOpenSignal()) {
      this.closePanel();
    } else {
      this.openPanel();
    }
  }

  async load(): Promise<void> {
    this.loadingSignal.set(true);
    this.errorSignal.set(null);
    try {
      const prefs = await this.api.getNotificationPreferences();
      this.preferencesSignal.set(prefs);
    } catch {
      this.errorSignal.set('Não foi possível carregar as preferências de notificação.');
    } finally {
      this.loadingSignal.set(false);
    }
  }

  async save(patch: Omit<NotificationPreferences, 'channelOverrides'>): Promise<boolean> {
    this.errorSignal.set(null);
    try {
      const updated = await this.api.updateNotificationPreferences(patch);
      this.preferencesSignal.update((current) => ({
        ...updated,
        channelOverrides: current?.channelOverrides ?? updated.channelOverrides,
      }));
      return true;
    } catch {
      this.errorSignal.set('Não foi possível salvar as preferências.');
      return false;
    }
  }

  async disableDndNow(): Promise<void> {
    const prefs = this.preferencesSignal();
    if (!prefs) {
      return;
    }
    await this.save({ ...prefs, dndEnabled: false });
  }

  async muteChannel(channelId: string, level: NotificationLevel, duration?: ChannelMuteDuration): Promise<void> {
    this.errorSignal.set(null);
    try {
      const override = await this.api.muteChannelNotifications(channelId, level, duration);
      this.upsertOverride(override);
    } catch {
      this.errorSignal.set('Não foi possível silenciar o canal.');
    }
  }

  async unmuteChannel(channelId: string): Promise<void> {
    this.errorSignal.set(null);
    try {
      await this.api.clearChannelNotificationOverride(channelId);
      this.preferencesSignal.update((current) =>
        current
          ? { ...current, channelOverrides: current.channelOverrides.filter((o) => o.channelId !== channelId) }
          : current,
      );
    } catch {
      this.errorSignal.set('Não foi possível remover o silêncio do canal.');
    }
  }

  private upsertOverride(override: ChannelNotificationOverride): void {
    this.preferencesSignal.update((current) => {
      if (!current) {
        return current;
      }
      const others = current.channelOverrides.filter((o) => o.channelId !== override.channelId);
      return { ...current, channelOverrides: [...others, override] };
    });
  }
}
