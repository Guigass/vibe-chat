import { Component, computed, input, output } from '@angular/core';
import { CdkMenu, CdkMenuItem, CdkMenuTrigger } from '@angular/cdk/menu';
import { Channel, ChannelMuteAction, PresenceStatus } from '../../models/chat.models';
import { Badge } from '../badge/badge';
import { VcTooltip } from '../tooltip/tooltip';

@Component({
  selector: 'vc-channel-item',
  standalone: true,
  imports: [Badge, VcTooltip, CdkMenuTrigger, CdkMenu, CdkMenuItem],
  template: `
    <button
      type="button"
      class="vc-channel"
      [class.vc-channel--active]="active()"
      [class.vc-channel--compact]="compact()"
      [class.vc-channel--muted]="muted()"
      [attr.aria-label]="compact() ? ariaLabel() : null"
      [vcTooltip]="compact() ? ariaLabel() : null"
      [tooltipDisabled]="!compact()"
      position="right"
      (click)="select.emit()"
    >
      @if (channel().isGroupDm) {
        <span class="vc-channel__stack" aria-hidden="true">
          @for (initial of stackedInitials(); track initial) {
            <span class="vc-channel__stack-dot">{{ initial }}</span>
          }
        </span>
      } @else if (presence()) {
        <span class="vc-channel__presence" [attr.data-status]="presence()" aria-hidden="true"></span>
      } @else {
        <span class="vc-channel__hash" aria-hidden="true">{{ prefix() }}</span>
      }
      <span class="vc-channel__main">
        <span class="vc-channel__name">{{ channel().name }}</span>
        @if (hasDraft()) {
          <span class="vc-channel__draft">Rascunho</span>
        }
      </span>
      @if (muted()) {
        <span class="vc-channel__muted-icon" aria-label="Silenciado" title="Silenciado">
          <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round">
            <path d="M6 8a6 6 0 0 1 10.24-4.24M18 8c0 7 3 9 3 9H3s3-2 3-9" />
            <path d="M10.3 21a1.94 1.94 0 0 0 3.4 0" />
            <line x1="2" y1="2" x2="22" y2="22" />
          </svg>
        </span>
      } @else if (channel().mentionCount && channel().mentionCount! > 0) {
        <vc-badge tone="warn">{{ channel().mentionCount }}</vc-badge>
      } @else if (channel().unreadCount > 0) {
        <vc-badge tone="accent">{{ channel().unreadCount }}</vc-badge>
      }
    </button>
    @if (!compact()) {
      <button
        type="button"
        class="vc-channel__kebab"
        aria-label="Notificações do canal"
        aria-haspopup="menu"
        [cdkMenuTriggerFor]="muteMenu"
        (click)="$event.stopPropagation()"
      >
        <span aria-hidden="true">⋮</span>
      </button>
    }

    <ng-template #muteMenu>
      <div class="vc-channel-menu" cdkMenu>
        <button type="button" cdkMenuItem (click)="muteAction.emit({ kind: 'mute', duration: 'OneHour' })">
          Silenciar por 1 hora
        </button>
        <button type="button" cdkMenuItem (click)="muteAction.emit({ kind: 'mute', duration: 'EightHours' })">
          Silenciar por 8 horas
        </button>
        <button type="button" cdkMenuItem (click)="muteAction.emit({ kind: 'mute', duration: 'UntilTomorrow' })">
          Silenciar até amanhã
        </button>
        <button type="button" cdkMenuItem (click)="muteAction.emit({ kind: 'mute', duration: 'Indefinite' })">
          Silenciar indefinidamente
        </button>
        <button type="button" cdkMenuItem (click)="muteAction.emit({ kind: 'all' })">
          Notificar todas as mensagens
        </button>
        @if (muted() || channelHasOverride()) {
          <button type="button" cdkMenuItem (click)="muteAction.emit({ kind: 'default' })">
            Usar o padrão
          </button>
        }
      </div>
    </ng-template>
  `,
  styles: `
    :host {
      position: relative;
      display: block;
    }
    .vc-channel {
      width: 100%;
      display: grid;
      grid-template-columns: auto 1fr auto;
      align-items: center;
      gap: var(--vc-space-row);
      min-height: var(--vc-density-row);
      padding: 0 0.7rem;
      border: 0;
      border-radius: var(--vc-radius-md);
      background: transparent;
      color: var(--vc-ink-muted);
      text-align: left;
      cursor: pointer;
      position: relative;
      transition:
        background var(--vc-dur-fast) var(--vc-ease-out),
        color var(--vc-dur-fast) var(--vc-ease-out);
    }
    .vc-channel:hover {
      background: color-mix(in srgb, var(--vc-brand) 10%, transparent);
      color: var(--vc-ink);
    }
    .vc-channel--active {
      background: color-mix(in srgb, var(--vc-brand) 16%, transparent);
      color: var(--vc-ink);
    }
    .vc-channel--active::before {
      content: '';
      position: absolute;
      left: 0;
      top: 20%;
      bottom: 20%;
      width: 3px;
      border-radius: 999px;
      background: var(--vc-brand);
    }
    .vc-channel__hash {
      font-family: var(--vc-font-mono);
      opacity: 0.7;
    }
    .vc-channel__stack {
      display: flex;
      width: 1.35rem;
    }
    .vc-channel__stack-dot {
      width: 0.85rem;
      height: 0.85rem;
      margin-left: -0.28rem;
      border-radius: 50%;
      display: grid;
      place-items: center;
      background: color-mix(in srgb, var(--vc-brand) 18%, transparent);
      color: var(--vc-brand);
      font-size: 0.55rem;
      font-weight: 700;
      text-transform: uppercase;
    }
    .vc-channel__stack-dot:first-child {
      margin-left: 0;
    }
    .vc-channel__presence {
      width: 0.55rem;
      height: 0.55rem;
      border-radius: 50%;
      background: var(--vc-presence-offline);
      justify-self: center;
    }
    .vc-channel__presence[data-status='online'] {
      background: var(--vc-presence-online);
    }
    .vc-channel__presence[data-status='away'] {
      background: var(--vc-presence-away);
    }
    .vc-channel__main {
      display: grid;
      gap: 0.05rem;
      min-width: 0;
    }
    .vc-channel__name {
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
      font-weight: 500;
    }
    .vc-channel__draft {
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
      font-size: 0.72rem;
      font-style: italic;
      color: var(--vc-ink-subtle);
      font-weight: 400;
    }
    .vc-channel--compact {
      grid-template-columns: 1fr;
      justify-items: center;
      padding: 0;
      width: 2.25rem;
      margin-inline: auto;
    }
    .vc-channel--compact .vc-channel__main,
    .vc-channel--compact vc-badge {
      display: none;
    }
    .vc-channel--compact.vc-channel--active::before {
      left: -0.35rem;
    }
    .vc-channel--compact .vc-channel__hash,
    .vc-channel--compact .vc-channel__presence {
      font-size: 0.95rem;
    }
    .vc-channel--muted {
      opacity: 0.6;
    }
    .vc-channel__muted-icon {
      display: inline-flex;
      color: var(--vc-ink-subtle);
    }
    .vc-channel__kebab {
      position: absolute;
      right: 0.35rem;
      top: 50%;
      transform: translateY(-50%);
      width: 1.4rem;
      height: 1.4rem;
      display: none;
      align-items: center;
      justify-content: center;
      border: 0;
      border-radius: var(--vc-radius-sm);
      background: var(--vc-surface-raised, var(--vc-surface));
      color: var(--vc-ink-muted);
      cursor: pointer;
      font-size: 0.9rem;
      line-height: 1;
    }
    :host:hover .vc-channel__kebab,
    :host:focus-within .vc-channel__kebab {
      display: flex;
    }
    .vc-channel__kebab:hover {
      color: var(--vc-ink);
    }
    .vc-channel-menu {
      display: flex;
      flex-direction: column;
      min-width: 13rem;
      padding: 0.3rem;
      border: 1px solid var(--vc-border);
      border-radius: var(--vc-radius-md);
      background: var(--vc-surface);
      box-shadow: var(--vc-shadow-md, 0 8px 24px color-mix(in srgb, var(--vc-ink) 18%, transparent));
    }
    .vc-channel-menu button {
      border: 0;
      background: transparent;
      color: var(--vc-ink);
      font: inherit;
      font-size: 0.84rem;
      text-align: left;
      padding: 0.45rem 0.65rem;
      border-radius: var(--vc-radius-sm);
      cursor: pointer;
    }
    .vc-channel-menu button:hover,
    .vc-channel-menu button:focus-visible {
      background: color-mix(in srgb, var(--vc-brand) 12%, transparent);
      outline: none;
    }
  `,
})
export class ChannelItem {
  readonly channel = input.required<Channel>();
  readonly active = input(false);
  readonly hasDraft = input(false);
  readonly compact = input(false);
  readonly presence = input<PresenceStatus | null>(null);
  /** Effective mute: a "None" channel override that hasn't expired. */
  readonly muted = input(false);
  /** Any channel override at all (muted or an "All" override) — controls "Usar o padrão" visibility. */
  readonly channelHasOverride = input(false);
  readonly select = output<void>();
  readonly muteAction = output<ChannelMuteAction>();

  readonly ariaLabel = computed(() => {
    const ch = this.channel();
    const parts = [ch.isDirect ? `@${ch.name}` : `#${ch.name}`];
    if (this.hasDraft()) {
      parts.push('rascunho');
    }
    if (this.muted()) {
      parts.push('silenciado');
    }
    if (ch.mentionCount && ch.mentionCount > 0) {
      parts.push(`${ch.mentionCount} menções`);
    } else if (ch.unreadCount > 0) {
      parts.push(`${ch.unreadCount} não lidas`);
    }
    return parts.join(', ');
  });

  prefix(): string {
    if (this.channel().isGroupDm) return '··';
    if (this.channel().isDirect) return '@';
    return this.channel().isPrivate ? '◦' : '#';
  }

  stackedInitials(): string[] {
    const names = this.channel().participantNames ?? [];
    return names.slice(0, 2).map((n) => (n.trim()[0] ?? '?').toUpperCase());
  }
}
