import { Injectable, computed, inject, signal } from '@angular/core';
import { ApiService } from '../../../core/api/api.service';
import { ChannelStore } from '../../../core/services/channel.store';
import {
  MAX_ATTACHMENTS_PER_MESSAGE,
  PendingAttachment,
  UPLOAD_CONCURRENCY,
  validateAttachmentFile,
} from './attachment-upload';

@Injectable({ providedIn: 'root' })
export class AttachmentQueueService {
  private readonly api = inject(ApiService);
  private readonly channels = inject(ChannelStore);

  private readonly itemsSignal = signal<PendingAttachment[]>([]);
  private readonly liveAnnouncerSignal = signal<string | null>(null);
  private readonly abortControllers = new Map<string, AbortController>();
  private uploadChain: Promise<void> = Promise.resolve();

  readonly items = this.itemsSignal.asReadonly();
  readonly liveAnnouncement = this.liveAnnouncerSignal.asReadonly();

  readonly readyAttachmentIds = computed(() =>
    this.itemsSignal()
      .filter((item) => item.status === 'ready' && item.attachmentId)
      .map((item) => item.attachmentId!),
  );

  readonly hasActiveUploads = computed(() =>
    this.itemsSignal().some((item) => item.status === 'uploading' || item.status === 'queued'),
  );

  readonly hasFailed = computed(() => this.itemsSignal().some((item) => item.status === 'failed'));

  readonly canAcceptMore = computed(
    () => this.itemsSignal().length < MAX_ATTACHMENTS_PER_MESSAGE,
  );

  readonly submitBlocked = computed(() => this.hasFailed());

  addFiles(files: File[]): string | null {
    const channelId = this.channels.activeChannel()?.id;
    if (!channelId || !files.length) return null;

    const current = this.itemsSignal();
    const remaining = MAX_ATTACHMENTS_PER_MESSAGE - current.length;
    if (remaining <= 0) {
      return `No máximo ${MAX_ATTACHMENTS_PER_MESSAGE} anexos por mensagem.`;
    }

    const accepted = files.slice(0, remaining);
    const skipped = files.length - accepted.length;
    const next: PendingAttachment[] = [];
    const errors: string[] = [];

    for (const file of accepted) {
      const validation = validateAttachmentFile(file);
      if (validation) {
        errors.push(`${validation.fileName}: ${validation.reason}`);
        continue;
      }
      next.push({
        localId: crypto.randomUUID(),
        file,
        status: 'queued',
        progress: 0,
      });
    }

    if (!next.length && errors.length) {
      return errors.join(' ');
    }

    this.itemsSignal.update((list) => [...list, ...next]);
    if (next.length) {
      const suffix = skipped > 0 ? ` (${skipped} ignorados pelo limite)` : '';
      this.announce(`${next.length} arquivo${next.length === 1 ? '' : 's'} adicionado${next.length === 1 ? '' : 's'}${suffix}`);
      this.scheduleUploads(channelId);
    }

    if (errors.length) {
      return errors.join(' ');
    }
    if (skipped > 0) {
      return `No máximo ${MAX_ATTACHMENTS_PER_MESSAGE} anexos por mensagem.`;
    }
    return null;
  }

  remove(localId: string): void {
    this.abortControllers.get(localId)?.abort();
    this.abortControllers.delete(localId);
    this.itemsSignal.update((list) => list.filter((item) => item.localId !== localId));
  }

  retry(localId: string): void {
    const channelId = this.channels.activeChannel()?.id;
    if (!channelId) return;
    this.patch(localId, { status: 'queued', progress: 0, error: undefined, attachmentId: undefined });
    this.scheduleUploads(channelId);
  }

  cancelUpload(localId: string): void {
    this.abortControllers.get(localId)?.abort();
    this.abortControllers.delete(localId);
    this.patch(localId, { status: 'failed', progress: 0, error: 'Upload cancelado' });
  }

  clear(): void {
    for (const controller of this.abortControllers.values()) {
      controller.abort();
    }
    this.abortControllers.clear();
    this.itemsSignal.set([]);
  }

  async waitForReady(): Promise<string[]> {
    while (this.hasActiveUploads()) {
      await new Promise((resolve) => setTimeout(resolve, 120));
    }
    return this.readyAttachmentIds();
  }

  private scheduleUploads(channelId: string): void {
    this.uploadChain = this.uploadChain.then(() => this.runUploadQueue(channelId));
  }

  private async runUploadQueue(channelId: string): Promise<void> {
    while (true) {
      const queued = this.itemsSignal().filter((item) => item.status === 'queued');
      if (!queued.length) return;

      const batch = queued.slice(0, UPLOAD_CONCURRENCY);
      await Promise.all(batch.map((item) => this.uploadOne(channelId, item.localId)));
    }
  }

  private async uploadOne(channelId: string, localId: string): Promise<void> {
    const item = this.itemsSignal().find((row) => row.localId === localId);
    if (!item || item.status !== 'queued') return;

    const controller = new AbortController();
    this.abortControllers.set(localId, controller);
    this.patch(localId, { status: 'uploading', progress: 0, error: undefined });

    try {
      const contentType = item.file.type || 'application/octet-stream';
      const initiated = await this.api.initiateAttachmentUpload({
        channelId,
        fileName: item.file.name,
        contentType,
        sizeBytes: item.file.size,
      });

      await this.api.uploadFileToPresignedUrl(
        initiated.uploadUrl,
        item.file,
        initiated.requiredHeaders ?? {},
        (progress) => this.patch(localId, { progress }),
        controller.signal,
      );

      const ready = await this.api.completeAttachmentUpload(channelId, initiated.attachmentId);
      this.patch(localId, {
        status: 'ready',
        progress: 100,
        attachmentId: ready.id,
      });
      this.announce(`${item.file.name} enviado`);
    } catch (error) {
      if (controller.signal.aborted) {
        this.patch(localId, { status: 'failed', progress: 0, error: 'Upload cancelado' });
        return;
      }
      const message = error instanceof Error ? error.message : 'Falha no upload';
      this.patch(localId, { status: 'failed', progress: 0, error: message });
    } finally {
      this.abortControllers.delete(localId);
    }
  }

  private patch(localId: string, patch: Partial<PendingAttachment>): void {
    this.itemsSignal.update((list) =>
      list.map((item) => (item.localId === localId ? { ...item, ...patch } : item)),
    );
  }

  private announce(message: string): void {
    this.liveAnnouncerSignal.set(message);
    setTimeout(() => this.liveAnnouncerSignal.set(null), 2500);
  }
}
