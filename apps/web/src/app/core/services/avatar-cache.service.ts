import { Injectable, inject } from '@angular/core';
import { ProfileApiService } from '../api/profile-api.service';

@Injectable({ providedIn: 'root' })
export class AvatarCache {
  private readonly profile = inject(ProfileApiService);
  private readonly urls = new Map<string, string>();
  private readonly pending = new Map<string, Promise<string | null>>();

  resolve(path: string | null | undefined): Promise<string | null> {
    if (!path) return Promise.resolve(null);
    const hit = this.urls.get(path);
    if (hit) return Promise.resolve(hit);
    const inflight = this.pending.get(path);
    if (inflight) return inflight;
    const task = this.profile
      .fetchAvatar(path)
      .then((blob) => {
        if (!blob) return null;
        const url = URL.createObjectURL(blob);
        this.urls.set(path, url);
        return url;
      })
      .finally(() => this.pending.delete(path));
    this.pending.set(path, task);
    return task;
  }
}
