import { Component, effect, inject, input, output } from '@angular/core';
import { ui } from '../core/i18n/strings';
import { ChannelStore } from '../core/services/channel.store';
import { CommitmentStore } from '../core/services/commitment.store';
import { KnowledgeStore } from '../core/services/knowledge.store';
import { FollowedThreadsStore } from '../core/services/followed-threads.store';
import { InboxStore } from '../core/services/inbox.store';
import { NotificationPreferencesStore } from '../core/services/notification-preferences.store';
import { PinStore } from '../core/services/pin.store';
import { SavedStore } from '../core/services/saved.store';
import { ThreadStore } from '../core/services/thread.store';
import { ChannelMembersPanel } from '../features/chat/channel-members-panel/channel-members-panel';
import { CommitmentPanel } from '../features/chat/commitment-panel/commitment-panel';
import { KnowledgePanel } from '../features/chat/knowledge-panel/knowledge-panel';
import { FollowedThreadsPanel } from '../features/chat/followed-threads-panel/followed-threads-panel';
import { InboxPanel } from '../features/chat/inbox-panel/inbox-panel';
import { NotificationPreferencesPanel } from '../features/chat/notification-preferences-panel/notification-preferences-panel';
import { PinsPanel } from '../features/chat/pins-panel/pins-panel';
import { SavedPanel } from '../features/chat/saved-panel/saved-panel';
import { ThreadPanel } from '../features/chat/thread-panel/thread-panel';

@Component({
  selector: 'vc-shell-context',
  standalone: true,
  imports: [
    ThreadPanel,
    ChannelMembersPanel,
    InboxPanel,
    SavedPanel,
    FollowedThreadsPanel,
    PinsPanel,
    NotificationPreferencesPanel,
    CommitmentPanel,
    KnowledgePanel,
  ],
  template: `
    @if (threads.open()) {
      <aside class="shell__context vc-anim-sidebar" [attr.aria-label]="ui.thread">
        <vc-thread-panel />
      </aside>
    } @else if (membersOpen()) {
      <aside class="shell__context vc-anim-sidebar" [attr.aria-label]="ui.channelMembers">
        <vc-channel-members-panel
          (close)="membersClose.emit()"
          (changed)="membersChanged.emit()"
          (left)="membersLeft.emit()"
        />
      </aside>
    } @else if (inbox.panelOpen()) {
      <aside class="shell__context vc-anim-sidebar" [attr.aria-label]="ui.inboxTitle">
        <vc-inbox-panel />
      </aside>
    } @else if (saved.panelOpen()) {
      <aside class="shell__context vc-anim-sidebar" [attr.aria-label]="ui.savedMessages">
        <vc-saved-panel />
      </aside>
    } @else if (followed.panelOpen()) {
      <aside class="shell__context vc-anim-sidebar" [attr.aria-label]="ui.channelFollowedThreads">
        <vc-followed-threads-panel />
      </aside>
    } @else if (pins.panelOpen()) {
      <aside class="shell__context vc-anim-sidebar" [attr.aria-label]="ui.pinnedMessages">
        <vc-pins-panel
          (close)="pins.closePanel()"
          (jump)="pins.jumpToPin($event)"
          (unpin)="onUnpin($event.messageId)"
        />
      </aside>
    } @else if (commitments.panelOpen()) {
      <aside class="shell__context vc-anim-sidebar" [attr.aria-label]="ui.commitmentPanel">
        <vc-commitment-panel />
      </aside>
    } @else if (knowledge.panelOpen()) {
      <aside class="shell__context vc-anim-sidebar" [attr.aria-label]="ui.knowledgePanel">
        <vc-knowledge-panel />
      </aside>
    } @else if (prefs.panelOpen()) {
      <aside class="shell__context vc-anim-sidebar" [attr.aria-label]="ui.settings">
        <vc-notification-preferences-panel />
      </aside>
    }
  `,
})
export class ShellContext {
  readonly ui = ui;
  readonly membersOpen = input(false);
  readonly membersClose = output<void>();
  readonly membersChanged = output<void>();
  readonly membersLeft = output<void>();
  readonly threads = inject(ThreadStore);
  readonly inbox = inject(InboxStore);
  readonly saved = inject(SavedStore);
  readonly followed = inject(FollowedThreadsStore);
  readonly pins = inject(PinStore);
  readonly prefs = inject(NotificationPreferencesStore);
  readonly commitments = inject(CommitmentStore);
  readonly knowledge = inject(KnowledgeStore);
  private readonly channels = inject(ChannelStore);

  constructor() {
    effect(() => {
      if (this.inbox.panelOpen() && this.membersOpen()) {
        this.membersClose.emit();
      }
      const otherOpen = this.threads.open()
        || this.membersOpen()
        || this.inbox.panelOpen()
        || this.saved.panelOpen()
        || this.followed.panelOpen()
        || this.pins.panelOpen()
        || this.commitments.panelOpen()
        || this.prefs.panelOpen();
      if (otherOpen && this.knowledge.panelOpen()) {
        this.knowledge.closePanel();
      }
    });
  }

  async onUnpin(messageId: string): Promise<void> {
    const channelId = this.channels.activeChannelId();
    if (!channelId) return;
    await this.pins.unpinMessage(channelId, messageId);
  }
}
