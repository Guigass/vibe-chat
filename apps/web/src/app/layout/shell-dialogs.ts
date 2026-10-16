import { Component, input, output } from '@angular/core';
import { CommitmentDialog } from '../features/chat/commitment-dialog/commitment-dialog';
import { TaskDialog } from '../features/chat/task-dialog/task-dialog';
import { MemberProfileDrawer } from '../features/chat/member-profile/member-profile-drawer';
import { UserStatusEditor } from '../features/chat/user-status/user-status-editor';

@Component({
  selector: 'vc-shell-dialogs',
  standalone: true,
  imports: [UserStatusEditor, MemberProfileDrawer, CommitmentDialog, TaskDialog],
  template: `
    @if (statusOpen()) {
      <vc-user-status-editor (closed)="statusClosed.emit()" />
    }
    <vc-member-profile-drawer />
    <vc-commitment-dialog />
    <vc-task-dialog />
  `,
})
export class ShellDialogs {
  readonly statusOpen = input(false);
  readonly statusClosed = output<void>();
}
