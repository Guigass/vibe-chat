import { Component, inject, input } from '@angular/core';
import { ui } from '../../../core/i18n/strings';
import { FollowedThreadsStore } from '../../../core/services/followed-threads.store';
import { CommitmentStore } from '../../../core/services/commitment.store';
import { KnowledgeStore } from '../../../core/services/knowledge.store';
import { InboxStore } from '../../../core/services/inbox.store';
import { NotificationPreferencesStore } from '../../../core/services/notification-preferences.store';
import { PinStore } from '../../../core/services/pin.store';
import { SavedStore } from '../../../core/services/saved.store';
import { ThreadStore } from '../../../core/services/thread.store';
import { Badge, VcTooltip } from '../../../shared/ui';

@Component({
  selector: 'vc-nav-shortcuts',
  standalone: true,
  imports: [Badge, VcTooltip],
  template: `
    <div class="channel-list__shortcuts" [class.channel-list__shortcuts--compact]="navCompact()">
      <button
        type="button"
        class="channel-list__saved"
        [class.channel-list__saved--compact]="navCompact()"
        [class.channel-list__saved--active]="inbox.panelOpen()"
        data-testid="inbox-nav"
        [attr.aria-label]="navCompact() ? ui.inboxTitle : null"
        [vcTooltip]="navCompact() ? ui.inboxTitle : null"
        [tooltipDisabled]="!navCompact()"
        position="right"
        (click)="openInbox()"
      >
        <span class="channel-list__saved-icon" aria-hidden="true">
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
            <path d="M4 6h16v12H4z" />
            <path d="m4 7 8 6 8-6" />
          </svg>
        </span>
        @if (!navCompact()) {
          <span class="channel-list__saved-copy">
            <span class="channel-list__saved-label">{{ ui.inboxTitle }}</span>
            <span class="channel-list__saved-hint">{{ ui.inboxHint }}</span>
          </span>
        }
        @if (inbox.openCount() > 0) {
          @if (navCompact()) {
            <span class="channel-list__saved-dot" aria-hidden="true"></span>
          } @else {
            <vc-badge tone="accent">{{ inbox.openCount() }}</vc-badge>
          }
        }
      </button>

      <button
        type="button"
        class="channel-list__saved"
        [class.channel-list__saved--compact]="navCompact()"
        [class.channel-list__saved--active]="saved.panelOpen()"
        data-testid="saved-nav"
        [attr.aria-label]="navCompact() ? ui.savedMessages : null"
        [vcTooltip]="navCompact() ? ui.savedMessages : null"
        [tooltipDisabled]="!navCompact()"
        position="right"
        (click)="openSaved()"
      >
        <span class="channel-list__saved-icon" aria-hidden="true">
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
            <path d="M6 4.5A1.5 1.5 0 0 1 7.5 3h9A1.5 1.5 0 0 1 18 4.5v15.2a.8.8 0 0 1-1.25.66L12 16.6l-4.75 3.76A.8.8 0 0 1 6 19.7V4.5Z" />
          </svg>
        </span>
        @if (!navCompact()) {
          <span class="channel-list__saved-copy">
            <span class="channel-list__saved-label">{{ ui.channelSaved }}</span>
            <span class="channel-list__saved-hint">{{ ui.channelPersonalView }}</span>
          </span>
        }
        @if (saved.pendingCount() > 0) {
          @if (navCompact()) {
            <span class="channel-list__saved-dot" aria-hidden="true"></span>
          } @else {
            <vc-badge tone="accent">{{ saved.pendingCount() }}</vc-badge>
          }
        }
      </button>

      <button
        type="button"
        class="channel-list__saved channel-list__followed"
        [class.channel-list__saved--compact]="navCompact()"
        [class.channel-list__saved--active]="followedThreads.panelOpen()"
        data-testid="followed-threads-nav"
        [attr.aria-label]="navCompact() ? ui.channelFollowedThreads : null"
        [vcTooltip]="navCompact() ? ui.channelFollowedThreads : null"
        [tooltipDisabled]="!navCompact()"
        position="right"
        (click)="openFollowedThreads()"
      >
        <span class="channel-list__saved-icon" aria-hidden="true">
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
            <path d="M4 5.5h11M4 12h16M4 18.5h11" />
          </svg>
        </span>
        @if (!navCompact()) {
          <span class="channel-list__saved-copy">
            <span class="channel-list__saved-label">{{ ui.channelFollowedThreads }}</span>
            <span class="channel-list__saved-hint">{{ ui.channelPersonalView }}</span>
          </span>
        }
        @if (followedThreads.unreadTotal() > 0) {
          @if (navCompact()) {
            <span class="channel-list__saved-dot" aria-hidden="true"></span>
          } @else {
            <vc-badge tone="accent">{{ followedThreads.unreadTotal() }}</vc-badge>
          }
        }
      </button>

      <button
        type="button"
        class="channel-list__saved"
        [class.channel-list__saved--compact]="navCompact()"
        [class.channel-list__saved--active]="commitments.panelOpen()"
        data-testid="commitments-nav"
        [attr.aria-label]="navCompact() ? ui.commitmentNav : null"
        [vcTooltip]="navCompact() ? ui.commitmentNav : null"
        [tooltipDisabled]="!navCompact()"
        position="right"
        (click)="openCommitments()"
      >
        <span class="channel-list__saved-icon" aria-hidden="true">
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
            <path d="M8 6h12M8 12h12M8 18h8" />
            <path d="m4 6 1.2 1.2L7.5 5M4 12l1.2 1.2L7.5 11" />
          </svg>
        </span>
        @if (!navCompact()) {
          <span class="channel-list__saved-copy">
            <span class="channel-list__saved-label">{{ ui.commitmentNav }}</span>
            <span class="channel-list__saved-hint">{{ ui.commitmentNavHint }}</span>
          </span>
        }
      </button>

      <button
        type="button"
        class="channel-list__saved"
        [class.channel-list__saved--compact]="navCompact()"
        [class.channel-list__saved--active]="knowledge.panelOpen()"
        data-testid="knowledge-nav"
        [attr.aria-label]="navCompact() ? ui.knowledgeNav : null"
        [vcTooltip]="navCompact() ? ui.knowledgeNav : null"
        [tooltipDisabled]="!navCompact()"
        position="right"
        (click)="openKnowledge()"
      >
        <span class="channel-list__saved-icon" aria-hidden="true">
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
            <path d="M6 4.5h9.5L19 8v11.5H6z" />
            <path d="M15 4.5V8h4M8.5 12h7M8.5 15.5h7" />
          </svg>
        </span>
        @if (!navCompact()) {
          <span class="channel-list__saved-copy">
            <span class="channel-list__saved-label">{{ ui.knowledgeNav }}</span>
            <span class="channel-list__saved-hint">{{ ui.knowledgeNavHint }}</span>
          </span>
        }
      </button>
    </div>
  `,
  styles: `
    .channel-list__shortcuts {
      display: grid;
      gap: 0.35rem;
      padding: 0 var(--vc-space-3) var(--vc-space-2);
    }
    .channel-list__shortcuts--compact { padding-inline: var(--vc-space-2); }
    .channel-list__saved {
      width: 100%;
      display: grid;
      grid-template-columns: auto 1fr auto;
      align-items: center;
      gap: var(--vc-space-2);
      min-height: calc(var(--vc-density-row) + 0.25rem);
      padding: 0.4rem 0.65rem;
      border: 1px solid var(--vc-border);
      border-radius: var(--vc-radius-md);
      background: transparent;
      color: var(--vc-ink);
      text-align: left;
      cursor: pointer;
      position: relative;
      font: inherit;
    }
    .channel-list__saved:hover {
      border-color: color-mix(in srgb, var(--vc-brand) 45%, var(--vc-border));
      background: color-mix(in srgb, var(--vc-brand) 8%, transparent);
    }
    .channel-list__saved--active {
      border-color: color-mix(in srgb, var(--vc-brand) 55%, var(--vc-border));
      background: color-mix(in srgb, var(--vc-brand) 12%, transparent);
    }
    .channel-list__saved:focus-visible { outline: 2px solid var(--vc-brand); outline-offset: 2px; }
    .channel-list__saved-icon { display: inline-grid; place-items: center; width: 1.35rem; height: 1.35rem; color: var(--vc-brand); }
    .channel-list__saved-copy { display: grid; min-width: 0; gap: 0.05rem; }
    .channel-list__saved-label { font-weight: 600; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .channel-list__saved-hint { font-size: 0.7rem; color: var(--vc-ink-subtle); font-weight: 400; }
    .channel-list__saved--compact {
      grid-template-columns: 1fr;
      width: 2.25rem;
      min-height: 2.25rem;
      margin-inline: auto;
      padding: 0;
      justify-items: center;
      border-color: transparent;
      background: transparent;
    }
    .channel-list__saved-dot {
      position: absolute;
      top: 0.2rem;
      right: 0.2rem;
      width: 0.45rem;
      height: 0.45rem;
      border-radius: 50%;
      background: var(--vc-brand);
      box-shadow: 0 0 0 2px var(--vc-surface-elevated);
    }
  `,
})
export class NavShortcuts {
  readonly ui = ui;
  readonly navCompact = input(false);
  readonly inbox = inject(InboxStore);
  readonly saved = inject(SavedStore);
  readonly followedThreads = inject(FollowedThreadsStore);
  readonly commitments = inject(CommitmentStore);
  readonly knowledge = inject(KnowledgeStore);
  private readonly threads = inject(ThreadStore);
  private readonly pins = inject(PinStore);
  private readonly notificationPrefs = inject(NotificationPreferencesStore);

  openInbox(): void {
    this.pins.closePanel();
    this.saved.closePanel();
    this.followedThreads.closePanel();
    this.commitments.closePanel();
    this.knowledge.closePanel();
    this.notificationPrefs.closePanel();
    this.inbox.openPanel();
  }

  openSaved(): void {
    this.pins.closePanel();
    this.followedThreads.closePanel();
    this.commitments.closePanel();
    this.knowledge.closePanel();
    this.inbox.closePanel();
    this.saved.openPanel();
  }

  openFollowedThreads(): void {
    this.pins.closePanel();
    this.saved.closePanel();
    this.commitments.closePanel();
    this.knowledge.closePanel();
    this.inbox.closePanel();
    this.followedThreads.openPanel();
  }

  openCommitments(): void {
    this.pins.closePanel();
    this.saved.closePanel();
    this.followedThreads.closePanel();
    this.knowledge.closePanel();
    this.inbox.closePanel();
    this.notificationPrefs.closePanel();
    this.commitments.openPanel();
  }

  openKnowledge(): void {
    this.threads.close();
    this.pins.closePanel();
    this.saved.closePanel();
    this.followedThreads.closePanel();
    this.commitments.closePanel();
    this.inbox.closePanel();
    this.notificationPrefs.closePanel();
    this.knowledge.openPanel();
  }
}
