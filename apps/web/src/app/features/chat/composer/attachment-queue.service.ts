import { Injectable, computed, inject, signal } from '@angular/core';
import { ApiService } from '../../../core/api/api.service';
import { ChannelStore } from '../../../core/services/channel.store';
import {
  MAX_ATTACHMENTS_PER_MESSAGE,
  PendingAttachment,
  UPLOAD_CONCURRENCY,
  extractVideoMetadata,
  isVideoContentType,
  resolveContentType,
  validateAttachmentFile,
  validateVideoAttachmentFile,
} from './attachment-upload';
import { normalizeAudioContentType } from './audio-recorder';
import { RecordedAudio } from './audio-recorder.service';
import type { DraftAttachmentMeta } from '../../../core/services/draft-storage';

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

    const remaining = MAX_ATTACHMENTS_PER_MESSAGE - this.itemsSignal().length;
    if (remaining <= 0) {
      return `No máximo ${MAX_ATTACHMENTS_PER_MESSAGE} anexos por mensagem.`;
    }

    const accepted = files.slice(0, remaining);
    const skipped = files.length - accepted.length;
    void this.enqueueAccepted(channelId, accepted, skipped);
    return skipped > 0 ? `No máximo ${MAX_ATTACHMENTS_PER_MESSAGE} anexos por mensagem.` : null;
  }

  private async enqueueAccepted(channelId: string, accepted: File[], skipped: number): Promise<void> {
    const next: PendingAttachment[] = [];
    const errors: string[] = [];

    for (const file of accepted) {
      const contentType = resolveContentType(file);
      if (isVideoContentType(contentType)) {
        const syncError = validateVideoAttachmentFile(file, contentType);
        if (syncError) {
          errors.push(`${syncError.fileName}: ${syncError.reason}`);
          continue;
        }
        const localId = crypto.randomUUID();
        next.push({
          localId,
          file,
          status: 'validating',
          progress: 0,
          uploadKind: 'Video',
          previewUrl: URL.createObjectURL(file),
        });
        continue;
      }

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
      this.announce(errors.join(' '));
      return;
    }

    this.itemsSignal.update((list) => [...list, ...next]);
    if (next.length) {
      const suffix = skipped > 0 ? ` (${skipped} ignorados pelo limite)` : '';
      this.announce(`${next.length} arquivo${next.length === 1 ? '' : 's'} adicionado${next.length === 1 ? '' : 's'}${suffix}`);
    }
    if (errors.length) {
      this.announce(errors.join(' '));
    }

    for (const item of next) {
      if (item.uploadKind === 'Video') {
        await this.validateVideoItem(channelId, item.localId);
      }
    }

    this.scheduleUploads(channelId);
  }

  private async validateVideoItem(channelId: string, localId: string): Promise<void> {
    const item = this.itemsSignal().find((row) => row.localId === localId);
    if (!item || item.uploadKind !== 'Video') return;

    try {
      const metadata = await extractVideoMetadata(item.file);
      const validation = validateVideoAttachmentFile(
        item.file,
        resolveContentType(item.file),
        metadata.durationMs,
      );
      if (validation) {
        this.revokePreview(item);
        this.patch(localId, {
          status: 'failed',
          progress: 0,
          error: validation.reason,
          previewUrl: undefined,
        });
        return;
      }
      this.patch(localId, {
        status: 'queued',
        durationMs: metadata.durationMs,
        width: metadata.width,
        height: metadata.height,
      });
      this.scheduleUploads(channelId);
    } catch (error) {
      this.revokePreview(item);
      const message = error instanceof Error ? error.message : 'Falha ao validar vídeo';
      this.patch(localId, {
        status: 'failed',
        progress: 0,
        error: message,
        previewUrl: undefined,
      });
    }
  }

  remove(localId: string): void {
    this.abortControllers.get(localId)?.abort();
    this.abortControllers.delete(localId);
    const item = this.itemsSignal().find((row) => row.localId === localId);
    if (item) this.revokePreview(item);
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
    for (const item of this.itemsSignal()) {
      this.revokePreview(item);
    }
    this.itemsSignal.set([]);
  }

  /** Snapshot of ready uploads for draft persistence (ids + display metadata, not bytes). */
  readyAttachmentMetas(): DraftAttachmentMeta[] {
    return this.itemsSignal()
      .filter((item) => item.status === 'ready' && item.attachmentId)
      .map((item) => ({
        attachmentId: item.attachmentId!,
        fileName: item.file.name,
        sizeBytes: item.restoredSizeBytes ?? item.file.size,
        contentType: resolveContentType(item.file),
      }));
  }

  /** Rehydrate ready attachments from a persisted draft (by server id). */
  restoreReady(metas: DraftAttachmentMeta[]): void {
    this.clear();
    if (!metas.length) return;
    const next: PendingAttachment[] = metas.slice(0, MAX_ATTACHMENTS_PER_MESSAGE).map((meta) => {
      const file = new File([], meta.fileName, { type: meta.contentType || 'application/octet-stream' });
      return {
        localId: crypto.randomUUID(),
        file,
        status: 'ready' as const,
        progress: 100,
        attachmentId: meta.attachmentId,
        restoredSizeBytes: meta.sizeBytes,
      };
    });
    this.itemsSignal.set(next);
  }

  async waitForReady(): Promise<string[]> {
    while (this.hasActiveUploads()) {
      await new Promise((resolve) => setTimeout(resolve, 120));
    }
    return this.readyAttachmentIds();
  }

  async uploadRecordedAudio(
    channelId: string,
    recorded: RecordedAudio,
  ): Promise<{ attachmentId?: string; error?: string }> {
    if (!this.canAcceptMore()) {
      return { error: `No máximo ${MAX_ATTACHMENTS_PER_MESSAGE} anexos por mensagem.` };
    }

    const contentType = normalizeAudioContentType(recorded.mimeType);
    const localId = crypto.randomUUID();
    const file = new File([recorded.blob], recorded.fileName, { type: contentType });
    this.itemsSignal.update((list) => [
      ...list,
      {
        localId,
        file,
        status: 'uploading',
        progress: 0,
      },
    ]);

    try {
      const initiated = await this.api.initiateAttachmentUpload({
        channelId,
        fileName: recorded.fileName,
        contentType,
        sizeBytes: recorded.blob.size,
        kind: 'Audio',
        durationMs: recorded.durationMs,
        waveform: recorded.waveform,
      });

      await this.api.uploadFileToPresignedUrl(
        initiated.uploadUrl,
        file,
        initiated.requiredHeaders ?? {},
        (progress) => this.patch(localId, { progress }),
      );

      const ready = await this.api.completeAttachmentUpload(channelId, initiated.attachmentId);
      this.patch(localId, {
        status: 'ready',
        progress: 100,
        attachmentId: ready.id,
      });
      this.announce('Áudio enviado');
      return { attachmentId: ready.id };
    } catch (error) {
      const message = error instanceof Error ? error.message : 'Falha no upload do áudio';
      this.patch(localId, { status: 'failed', progress: 0, error: message });
      return { error: message };
    }
  }

  private scheduleUploads(channelId: string): void {
    this.uploadChain = this.uploadChain.then(() => this.runUploadQueue(channelId));
  }

  private async runUploadQueue(channelId: string): Promise<void> {
    while (true) {
      const queued = this.itemsSignal().filter(
        (item) => item.status === 'queued' && (item.uploadKind !== 'Video' || item.durationMs),
      );
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
      const contentType = resolveContentType(item.file);
      const isVideo = item.uploadKind === 'Video' || isVideoContentType(contentType);
      const initiated = await this.api.initiateAttachmentUpload({
        channelId,
        fileName: item.file.name,
        contentType,
        sizeBytes: item.file.size,
        kind: isVideo ? 'Video' : undefined,
        durationMs: isVideo ? item.durationMs : undefined,
        width: isVideo ? item.width : undefined,
        height: isVideo ? item.height : undefined,
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

  private revokePreview(item: PendingAttachment): void {
    if (item.previewUrl) {
      URL.revokeObjectURL(item.previewUrl);
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
