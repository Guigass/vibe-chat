import { Component, inject, input } from '@angular/core';
import { ui } from '../../../core/i18n/strings';
import { CanvasSession } from './canvas-session';

@Component({
  selector: 'vc-canvas-editor',
  standalone: true,
  template: `
    <section class="canvas" [attr.aria-label]="ui.canvasEditor" data-testid="canvas-editor">
      <p data-testid="canvas-status">{{ statusLabel() }}</p>
      <p>{{ ui.canvasPeers }} <span data-testid="canvas-peers">{{ peerLine() }}</span></p>
      @if (session.error(); as error) {
        <p role="alert">{{ error }}</p>
      }
      <label>
        {{ ui.canvasEditor }}
        <textarea
          data-testid="canvas-body"
          rows="8"
          [attr.aria-label]="ui.canvasEditor"
          [readOnly]="!canEdit()"
          [value]="session.body()"
          (input)="onInput($event)"
          (keyup)="onCaret($event)"
          (click)="onCaret($event)"
        ></textarea>
      </label>
      @if (canEdit()) {
        <button type="button" data-testid="canvas-undo" (click)="session.undo()">{{ ui.canvasUndo }}</button>
        <button type="button" data-testid="canvas-snapshot" (click)="saveSnapshot()">{{ ui.canvasSnapshot }}</button>
      }
      <h3>{{ ui.canvasHistory }}</h3>
      <ul>
        @for (item of session.snapshots(); track item.id) {
          <li>
            <span>{{ item.createdAt }}</span>
            @if (canManage()) {
              <button type="button" (click)="restore(item.id)">{{ ui.canvasRestore }}</button>
            }
          </li>
        } @empty {
          <li>{{ ui.canvasHistoryEmpty }}</li>
        }
      </ul>
    </section>
  `,
  styles: `
    .canvas { display: grid; gap: var(--vc-space-2); min-width: 0; }
    .canvas p, .canvas h3 { margin: 0; }
    .canvas h3 { font-size: var(--vc-text-sm); }
    .canvas label { display: grid; gap: var(--vc-space-1); }
    .canvas textarea, .canvas button {
      font: inherit;
      color: var(--vc-ink);
      background: transparent;
      border: 1px solid var(--vc-border);
      border-radius: var(--vc-radius-md);
      padding: 0.35rem 0.5rem;
      width: 100%;
      min-width: 0;
    }
    .canvas button { cursor: pointer; text-align: left; width: auto; }
    .canvas textarea:focus-visible, .canvas button:focus-visible {
      outline: 2px solid var(--vc-brand);
      outline-offset: 2px;
    }
    .canvas ul { list-style: none; margin: 0; padding: 0; display: grid; gap: var(--vc-space-1); }
    @media (prefers-reduced-motion: reduce) {
      .canvas textarea, .canvas button { transition: none; }
    }
  `,
})
export class CanvasEditor {
  readonly ui = ui;
  readonly session = inject(CanvasSession);
  readonly canEdit = input(false);
  readonly canManage = input(false);

  statusLabel(): string {
    switch (this.session.status()) {
      case 'offline':
        return ui.canvasStatusOffline;
      case 'reconnecting':
        return ui.canvasStatusReconnecting;
      default:
        return ui.canvasStatusOnline;
    }
  }

  peerLine(): string {
    const labels = this.session.peers().map((peer) => peer.label);
    return labels.length === 0 ? ui.canvasPeersEmpty : labels.join(', ');
  }

  onInput(event: Event): void {
    const target = event.target as HTMLTextAreaElement;
    this.session.setCursor(target.selectionStart ?? 0);
    this.session.edit(target.value);
  }

  onCaret(event: Event): void {
    const target = event.target as HTMLTextAreaElement;
    this.session.setCursor(target.selectionStart ?? 0);
  }

  saveSnapshot(): void {
    void this.session.snapshot();
  }

  restore(snapshotId: string): void {
    if (!window.confirm(ui.canvasRestoreConfirm)) {
      return;
    }
    void this.session.restore(snapshotId);
  }
}
