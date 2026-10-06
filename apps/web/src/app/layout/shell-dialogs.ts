import { Component, input, output } from '@angular/core';
import { MemberProfileDrawer } from '../features/chat/member-profile/member-profile-drawer';
import { UserStatusEditor } from '../features/chat/user-status/user-status-editor';

@Component({
  selector: 'vc-shell-dialogs',
  standalone: true,
  imports: [UserStatusEditor, MemberProfileDrawer],
  template: `
    @if (statusOpen()) {
      <vc-user-status-editor (closed)="statusClosed.emit()" />
    }
    <vc-member-profile-drawer />
  `,
})
export class ShellDialogs {
  readonly statusOpen = input(false);
  readonly statusClosed = output<void>();
}
