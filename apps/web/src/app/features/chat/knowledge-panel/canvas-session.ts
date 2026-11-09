import { Injectable, inject, signal } from '@angular/core';
import * as Y from 'yjs';
import { translateErrorCode, ui } from '../../../core/i18n/strings';
import {
  CanvasApiService,
  CanvasPeer,
  CanvasPublish,
  CanvasSnapshot,
  CanvasState,
} from '../../../core/api/canvas-api.service';
import { applyString, base64ToBytes, bytesToBase64, canvasTextName } from './canvas-doc';

export type CanvasLink = 'online' | 'offline' | 'reconnecting';

@Injectable({ providedIn: 'root' })
export class CanvasSession {
  private readonly api = inject(CanvasApiService);
  private doc: Y.Doc | null = null;
  private undoManager: Y.UndoManager | null = null;
  private timer = 0;
  private queue: CanvasPublish[] = [];
  private epoch = 0;
  private seq = 0;
  private cursor = 0;
  private workspace = '';
  private page = '';
  private opening: Promise<void> | null = null;
  private generation = 0;

  readonly active = signal(false);
  readonly status = signal<CanvasLink>('online');
  readonly body = signal('');
  readonly peers = signal<CanvasPeer[]>([]);
  readonly snapshots = signal<CanvasSnapshot[]>([]);
  readonly error = signal<string | null>(null);
  private committedBody = '';

  committed(): string {
    return this.committedBody;
  }

  async open(workspaceId: string, pageId: string): Promise<void> {
    if (this.workspace === workspaceId && this.page === pageId && (this.active() || this.opening)) {
      return this.opening ?? Promise.resolve();
    }
    this.discard();
    const generation = ++this.generation;
    this.workspace = workspaceId;
    this.page = pageId;
    this.opening = this.load(generation);
    try {
      await this.opening;
    } finally {
      if (this.opening && generation === this.generation) {
        this.opening = null;
      }
    }
  }

  close(): void {
    this.generation++;
    this.discard();
    this.workspace = '';
    this.page = '';
  }

  private discard(): void {
    this.stop();
    this.doc?.destroy();
    this.doc = null;
    this.undoManager = null;
    this.queue = [];
    this.active.set(false);
  }

  edit(next: string): void {
    const doc = this.doc;
    if (!doc) {
      return;
    }
    doc.transact(() => applyString(doc.getText(canvasTextName), next), 'local');
    this.body.set(doc.getText(canvasTextName).toString());
  }

  undo(): void {
    this.undoManager?.undo();
    if (this.doc) {
      this.body.set(this.doc.getText(canvasTextName).toString());
    }
  }

  setCursor(cursor: number): void {
    this.cursor = cursor;
  }

  async snapshot(): Promise<void> {
    if (!this.page) {
      return;
    }
    const saved = await this.api.snapshot(this.workspace, this.page);
    this.snapshots.update((items) => [saved, ...items.filter((item) => item.id !== saved.id)]);
  }

  async restore(snapshotId: string): Promise<void> {
    if (!this.page) {
      return;
    }
    const result = await this.api.restore(this.workspace, this.page, snapshotId);
    this.queue = [];
    this.replace({
      wireVersion: 1,
      epoch: result.epoch,
      seq: result.seq,
      compactedThroughSeq: result.seq,
      stateBase64: result.stateBase64,
      body: result.body,
      peers: this.peers(),
      snapshots: this.snapshots(),
    });
  }

  private async load(generation: number): Promise<void> {
    try {
      const state = await this.api.open(this.workspace, this.page);
      if (generation !== this.generation) {
        return;
      }
      this.queue = [];
      this.replace(state);
      this.active.set(true);
      this.status.set('online');
      this.error.set(null);
      await this.flush();
    } catch (error) {
      if (generation !== this.generation) {
        return;
      }
      this.active.set(false);
      const code = this.code(error);
      if (code !== 'CanvasRealtimeDisabled' && code !== 'KnowledgeDisabled') {
        this.error.set(translateErrorCode(code ?? '') || ui.canvasSaveError);
      }
    }
  }

  private replace(state: CanvasState): void {
    this.stop();
    this.doc?.destroy();
    const doc = new Y.Doc();
    this.doc = doc;
    doc.on('update', (update: Uint8Array, origin: unknown) => {
      if (origin === 'remote' || !this.page) {
        return;
      }
      this.queue.push({
        wireVersion: 1,
        idempotencyKey: crypto.randomUUID(),
        epoch: this.epoch,
        updateBase64: bytesToBase64(update),
      });
      void this.flush();
    });
    Y.applyUpdate(doc, base64ToBytes(state.stateBase64), 'remote');
    this.undoManager = new Y.UndoManager(doc.getText(canvasTextName));
    this.epoch = state.epoch;
    this.seq = state.seq;
    this.body.set(state.body);
    this.committedBody = state.body;
    this.peers.set(state.peers);
    this.snapshots.set(state.snapshots);
    this.start();
  }

  private start(): void {
    this.stop();
    this.timer = window.setInterval(() => void this.pull(), 4000);
  }

  private stop(): void {
    if (this.timer) {
      window.clearInterval(this.timer);
      this.timer = 0;
    }
  }

  private async pull(): Promise<void> {
    if (!this.page || !this.doc) {
      return;
    }
    try {
      const state = await this.api.open(this.workspace, this.page);
      this.peers.set(state.peers);
      this.snapshots.set(state.snapshots);
      if (state.epoch !== this.epoch || state.seq < this.seq) {
        this.queue = [];
        this.replace(state);
      } else if (state.seq > this.seq) {
        try {
          const missed = await this.api.updates(this.workspace, this.page, this.seq, this.epoch);
          for (const item of missed.items) {
            Y.applyUpdate(this.doc, base64ToBytes(item.updateBase64), 'remote');
            this.seq = item.seq;
          }
        } catch {
          this.queue = [];
          this.replace(state);
        }
        this.body.set(this.doc.getText(canvasTextName).toString());
        this.committedBody = state.body;
      }
      await this.api.presence(this.workspace, this.page, this.cursor);
      this.status.set('online');
      await this.flush();
    } catch {
      this.status.set(navigator.onLine ? 'reconnecting' : 'offline');
    }
  }

  private async flush(): Promise<void> {
    while (this.queue.length > 0 && this.page) {
      const next = this.queue[0];
      try {
        const result = await this.api.publish(this.workspace, this.page, { ...next, epoch: this.epoch });
        this.queue.shift();
        this.epoch = result.epoch;
        this.seq = result.seq;
        this.committedBody = result.body;
        if (result.body !== this.doc?.getText(canvasTextName).toString()) {
          this.replace({
            wireVersion: 1,
            epoch: result.epoch,
            seq: result.seq,
            compactedThroughSeq: result.seq,
            stateBase64: result.stateBase64,
            body: result.body,
            peers: this.peers(),
            snapshots: this.snapshots(),
          });
        }
        this.status.set('online');
        this.error.set(null);
      } catch (error) {
        const code = this.code(error);
        if (code === 'CanvasEpochMismatch' || code === 'CanvasResync') {
          this.queue = [];
          await this.load(this.generation);
          return;
        }
        this.status.set(navigator.onLine ? 'reconnecting' : 'offline');
        this.error.set(translateErrorCode(code ?? '') || ui.canvasSaveError);
        return;
      }
    }
  }

  private code(error: unknown): string | null {
    if (!(error instanceof Error) || !error.message) {
      return null;
    }
    try {
      const parsed = JSON.parse(error.message) as { error?: string };
      return parsed.error ?? null;
    } catch {
      return null;
    }
  }
}
