import { A11yModule } from '@angular/cdk/a11y';
import { Component, effect, inject, signal } from '@angular/core';
import { translateErrorCode, ui } from '../../../core/i18n/strings';
import { MemberProfileStore } from '../../../core/services/member-profile.store';
import { Avatar } from '../../../shared/ui/avatar/avatar';

@Component({
  selector: 'vc-member-profile-drawer',
  standalone: true,
  imports: [A11yModule, Avatar],
  template: `
    @if (store.openUserId()) {
      <div class="profile-drawer__backdrop" (click)="store.close()"></div>
      <section
        class="profile-drawer"
        role="dialog"
        aria-modal="true"
        cdkTrapFocus
        [cdkTrapFocusAutoCapture]="true"
        [attr.aria-labelledby]="titleId"
        data-testid="member-profile-drawer"
      >
        <header class="profile-drawer__head">
          <h2 [id]="titleId">{{ store.isMine() ? ui.profileMine : ui.profileTitle }}</h2>
          <button type="button" (click)="store.close()">{{ ui.closePanel }}</button>
        </header>

        @if (store.target(); as card) {
          <vc-avatar [name]="card.displayName" [authPath]="card.avatarUrl" [size]="64" />
          @if (store.isMine()) {
            <label>
              <span>{{ ui.profileName }}</span>
              <input data-testid="profile-name" [value]="name()" maxlength="80" (input)="name.set(read($event))" />
            </label>
            <label>
              <span>{{ ui.profileJobTitle }}</span>
              <input data-testid="profile-job" [value]="job()" maxlength="80" (input)="job.set(read($event))" />
            </label>
            <label>
              <span>{{ ui.profileAbout }}</span>
              <textarea data-testid="profile-about" [value]="about()" maxlength="500" rows="3" (input)="about.set(read($event))"></textarea>
            </label>
            <label>
              <span>{{ ui.profileHighlight }}</span>
              <input data-testid="profile-highlight" [value]="highlight()" maxlength="160" (input)="highlight.set(read($event))" />
            </label>
            <label class="profile-drawer__file">
              <span>{{ ui.profileAvatarChange }}</span>
              <input data-testid="profile-avatar" type="file" accept="image/png,image/jpeg,image/webp,image/gif" (change)="onFile($event)" />
            </label>
            @if (card.avatarUrl) {
              <button type="button" data-testid="profile-avatar-remove" (click)="store.removeAvatar()">{{ ui.profileAvatarRemove }}</button>
            }
            <div class="profile-drawer__actions">
              <button type="button" data-testid="profile-save" [disabled]="store.loading()" (click)="save()">
                {{ store.loading() ? ui.profileSaving : ui.profileSave }}
              </button>
            </div>
            @if (store.saved()) {
              <p role="status">{{ ui.profileSaved }}</p>
            }
          } @else {
            <p class="profile-drawer__name">{{ card.displayName }}</p>
            <p>{{ card.jobTitle || ui.profileEmpty }}</p>
            <p>{{ card.about || ui.profileEmpty }}</p>
            <p>{{ card.highlightMessage || ui.profileEmpty }}</p>
            <button type="button" data-testid="profile-message" (click)="store.message()">{{ ui.profileMessage }}</button>
          }
        }

        @if (store.error(); as code) {
          <p class="profile-drawer__error" role="alert">{{ errorText(code) }}</p>
        }
      </section>
    }
  `,
  styles: `
    :host { position: fixed; inset: 0; z-index: 40; display: contents; }
    .profile-drawer__backdrop {
      position: fixed; inset: 0; border: 0;
      background: color-mix(in srgb, var(--vc-ink) 35%, transparent);
    }
    .profile-drawer {
      position: fixed; top: 0; right: 0; bottom: 0; width: min(24rem, 100%);
      overflow: auto; display: grid; align-content: start; gap: 0.75rem;
      padding: var(--vc-space-4); border-left: 1px solid var(--vc-border);
      background: var(--vc-surface-elevated); color: var(--vc-ink);
    }
    .profile-drawer__head, .profile-drawer__actions { display: flex; justify-content: space-between; gap: 0.5rem; }
    .profile-drawer h2 { margin: 0; font-family: var(--vc-font-display); font-size: 1.15rem; }
    .profile-drawer label { display: grid; gap: 0.25rem; }
    .profile-drawer input, .profile-drawer textarea {
      min-height: 2.5rem; border: 1px solid var(--vc-border); border-radius: var(--vc-radius-sm);
      background: var(--vc-surface); color: var(--vc-ink); padding: 0.4rem 0.6rem; font: inherit;
    }
    .profile-drawer button {
      border: 1px solid var(--vc-border); background: transparent; color: var(--vc-ink);
      border-radius: var(--vc-radius-sm); min-height: 2.25rem; padding: 0.35rem 0.7rem; font: inherit; cursor: pointer;
    }
    .profile-drawer__name { margin: 0; font-weight: 600; }
    .profile-drawer p { margin: 0; }
    .profile-drawer__error { color: var(--vc-danger, #b42318); }
  `,
})
export class MemberProfileDrawer {
  readonly ui = ui;
  readonly titleId = 'member-profile-title';
  readonly store = inject(MemberProfileStore);
  readonly name = signal('');
  readonly job = signal('');
  readonly about = signal('');
  readonly highlight = signal('');

  constructor() {
    effect(() => {
      const card = this.store.target();
      if (!card || !this.store.isMine()) return;
      this.name.set(card.displayName);
      this.job.set(card.jobTitle ?? '');
      this.about.set(card.about ?? '');
      this.highlight.set(card.highlightMessage ?? '');
    });
  }

  read(event: Event): string {
    return (event.target as HTMLInputElement | HTMLTextAreaElement).value;
  }

  errorText(code: string): string {
    const translated = translateErrorCode(code);
    return translated === code ? ui.profileError : translated;
  }

  async save(): Promise<void> {
    await this.store.save({
      displayName: this.name().trim(),
      jobTitle: this.job().trim(),
      about: this.about().trim(),
      highlightMessage: this.highlight().trim(),
    });
  }

  async onFile(event: Event): Promise<void> {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (file) await this.store.upload(file);
  }
}
