import { ui } from '../../core/i18n/strings';
import { PresenceStatus } from '../models/chat.models';

export type UserStatusStateName = 'focus' | 'meeting' | 'vacation' | 'custom';
export type AvailabilityKind = 'available' | 'away' | 'busy' | 'vacation' | 'offline';

export interface UserStatusBody {
  state: UserStatusStateName;
  emoji: string;
  text: string;
  clearAtEndOfDay: boolean;
  expiresAt: string | null;
}

export interface MemberAvailability {
  userId: string;
  presence: PresenceStatus;
  availability: AvailabilityKind;
  status: UserStatusBody | null;
}

export interface UserStatusChangedEvent {
  userId: string;
  tenantId?: string;
  presence: PresenceStatus;
  availability: AvailabilityKind;
  status: UserStatusBody | null;
}

const BUSY_STATES = new Set<UserStatusStateName>(['focus', 'meeting']);

export function isStatusActive(status: UserStatusBody | null | undefined, nowMs: number): boolean {
  if (!status) return false;
  if (!status.expiresAt) return true;
  const expires = Date.parse(status.expiresAt);
  return Number.isFinite(expires) && expires > nowMs;
}

/** Mirrors UserStatusRules.Derive. DND is already folded into a busy availability when status is not focus/meeting. */
export function deriveAvailability(
  presence: PresenceStatus,
  dndActive: boolean,
  state: UserStatusStateName | null,
  statusActive: boolean,
): AvailabilityKind {
  if (dndActive) return 'busy';
  if (statusActive && state === 'vacation') return 'vacation';
  if (statusActive && state && BUSY_STATES.has(state)) return 'busy';
  if (presence === 'online') return 'available';
  if (presence === 'away') return 'away';
  return 'offline';
}

export function availabilityLabel(kind: AvailabilityKind): string {
  switch (kind) {
    case 'available':
      return ui.statusAvailable;
    case 'away':
      return ui.statusAway;
    case 'busy':
      return ui.statusBusy;
    case 'vacation':
      return ui.statusVacation;
    default:
      return ui.statusOffline;
  }
}

export function statusLine(entry: MemberAvailability | null | undefined, nowMs: number): string {
  if (!entry) return '';
  const active = isStatusActive(entry.status, nowMs) ? entry.status : null;
  if (active) {
    const text = `${active.emoji} ${active.text}`.trim();
    if (text) return text;
  }
  return availabilityLabel(entry.availability);
}

export function statusEmoji(entry: MemberAvailability | null | undefined, nowMs: number): string | null {
  if (!entry || !isStatusActive(entry.status, nowMs)) return null;
  const emoji = entry.status?.emoji?.trim();
  return emoji ? emoji : null;
}

/** Full label for screen readers. Visual callers truncate with CSS. */
export function statusAccessibleLabel(entry: MemberAvailability | null | undefined, nowMs: number): string {
  return statusLine(entry, nowMs);
}

export function withPresence(entry: MemberAvailability, presence: PresenceStatus, nowMs: number): MemberAvailability {
  const active = isStatusActive(entry.status, nowMs);
  const state = active ? entry.status?.state ?? null : null;
  const dnd =
    entry.availability === 'busy' && !(active && state !== null && BUSY_STATES.has(state));
  return {
    ...entry,
    presence,
    availability: deriveAvailability(presence, dnd, state, active),
  };
}
