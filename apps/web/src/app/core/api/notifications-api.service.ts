import { Injectable } from '@angular/core';
import { HttpApiClient } from './http-api.client';
import {
  PushPublicKey,
  PushDevice,
  NotificationPreferences,
  ChannelNotificationOverride,
  NotificationLevel,
  ChannelMuteDuration,
} from '../../shared/models/chat.models';
import {
  mapChannelNotificationOverride,
  mapNotificationPreferences,
} from '../../shared/notifications/notification-preferences';

@Injectable({ providedIn: 'root' })
export class NotificationsApiService extends HttpApiClient {
  async getPushPublicKey(): Promise<PushPublicKey> {
    return this.request<PushPublicKey>('/api/v1/notifications/push/public-key');
  }

  async listPushSubscriptions(): Promise<PushDevice[]> {
    const rows = await this.request<PushDevice[]>('/api/v1/notifications/push/subscriptions');
    return rows ?? [];
  }

  async registerPushSubscription(input: {
    endpoint: string;
    p256dh: string;
    auth: string;
    userAgent?: string;
  }): Promise<PushDevice> {
    return this.request<PushDevice>('/api/v1/notifications/push/subscriptions', {
      method: 'POST',
      body: JSON.stringify(input),
    });
  }

  async deletePushSubscription(id: string): Promise<void> {
    await this.request(`/api/v1/notifications/push/subscriptions/${id}`, { method: 'DELETE' });
  }

  async getNotificationPreferences(): Promise<NotificationPreferences> {
    const dto = await this.request<unknown>('/api/v1/notifications/preferences');
    return mapNotificationPreferences(dto);
  }

  async updateNotificationPreferences(
    patch: Omit<NotificationPreferences, 'channelOverrides' | 'followAllThreadsChannelIds'>,
  ): Promise<NotificationPreferences> {
    const dto = await this.request<unknown>('/api/v1/notifications/preferences', {
      method: 'PUT',
      body: JSON.stringify(patch),
    });
    return mapNotificationPreferences(dto);
  }

  async muteChannelNotifications(
    channelId: string,
    level: NotificationLevel,
    duration?: ChannelMuteDuration,
  ): Promise<ChannelNotificationOverride> {
    const dto = await this.request<unknown>(
      `/api/v1/notifications/preferences/channels/${channelId}`,
      { method: 'PUT', body: JSON.stringify({ level, duration: duration ?? null }) },
    );
    return (
      mapChannelNotificationOverride(dto) ?? {
        channelId,
        level,
        mutedUntil: null,
      }
    );
  }

  async clearChannelNotificationOverride(channelId: string): Promise<void> {
    await this.request(`/api/v1/notifications/preferences/channels/${channelId}`, { method: 'DELETE' });
  }
}
