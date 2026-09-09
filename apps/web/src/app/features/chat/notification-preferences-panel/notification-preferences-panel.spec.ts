import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it, vi } from 'vitest';
import { AuthService } from '../../../core/auth/auth.service';
import { LocaleService } from '../../../core/i18n/locale.service';
import { ChannelStore } from '../../../core/services/channel.store';
import { NotificationPreferencesStore } from '../../../core/services/notification-preferences.store';
import { PushNotificationService } from '../../../core/services/push-notification.service';
import { mapNotificationPreferences } from '../../../shared/notifications/notification-preferences';
import { NotificationPreferencesPanel } from './notification-preferences-panel';

describe('NotificationPreferencesPanel', () => {
  it('hydrates PascalCase prefs and keeps the current user out of priority contacts', async () => {
    const preferences = signal(
      mapNotificationPreferences({
        Level: 'All',
        HidePreview: true,
        DndEnabled: true,
        DndStart: '21:00:00',
        DndEnd: '07:00:00',
        DndDays: 0,
        TimeZone: 'America/Sao_Paulo',
        DigestEnabled: false,
        PriorityContactUserIds: ['u-alice', 'u-bob'],
        ChannelOverrides: [],
      }),
    );

    await TestBed.configureTestingModule({
      imports: [NotificationPreferencesPanel],
      providers: [
        {
          provide: NotificationPreferencesStore,
          useValue: {
            preferences,
            loading: () => false,
            error: () => null,
            closePanel: vi.fn(),
            save: vi.fn().mockResolvedValue(true),
          },
        },
        {
          provide: ChannelStore,
          useValue: {
            activeWorkspace: () => ({ name: 'Acme' }),
            peerCandidates: () => [
              { userId: 'u-bob', displayName: 'Bob Santos', email: 'bob@vibechat.local', role: 'Member' },
              { userId: 'u-carol', displayName: 'Carol Lima', email: 'carol@vibechat.local', role: 'Member' },
              ...Array.from({ length: 40 }, (_, index) => ({
                userId: `u-extra-${index}`,
                displayName: `Membro ${index}`,
                email: `m${index}@vibechat.local`,
                role: 'Member',
              })),
            ],
          },
        },
        {
          provide: AuthService,
          useValue: { profile: () => ({ id: 'u-alice', name: 'Alice' }) },
        },
        {
          provide: LocaleService,
          useValue: { locale: () => 'pt-BR', apply: vi.fn() },
        },
        {
          provide: PushNotificationService,
          useValue: {
            devices: () => [],
            refreshDevices: vi.fn().mockResolvedValue(undefined),
            removeDevice: vi.fn(),
          },
        },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(NotificationPreferencesPanel);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    const allRadio = host.querySelector<HTMLInputElement>('input[name="notif-level"]');
    expect(allRadio?.checked).toBe(true);
    expect(host.querySelector<HTMLInputElement>('input[type="time"]')?.value).toBe('21:00');
    expect(host.querySelector<HTMLSelectElement>('[data-testid="notif-timezone"]')?.value).toBe(
      'America/Sao_Paulo',
    );
    expect(host.querySelectorAll('[data-testid="notif-timezone"] option').length).toBeGreaterThan(1);
    expect(host.querySelector('[data-testid="notif-priority"]')?.textContent).toContain('Bob Santos');
    expect(host.querySelector('[data-testid="notif-priority"]')?.textContent).not.toContain('Alice');
    expect(host.textContent).not.toContain('Membro 12');
    expect(host.querySelector('[data-testid="notif-priority-suggest"]')).toBeNull();
    expect(fixture.componentInstance.isPriorityContact('u-bob')).toBe(true);
    expect(fixture.componentInstance.isPriorityContact('u-alice')).toBe(false);

    fixture.componentInstance.contactQuery.set('Carol');
    fixture.detectChanges();
    expect(host.querySelector('[data-testid="notif-priority-suggest"]')?.textContent).toContain('Carol Lima');
    expect(host.querySelectorAll('[data-testid="notif-priority-suggest"] button').length).toBe(1);
  });

  it('does not nest a form inside the panel', async () => {
    await TestBed.configureTestingModule({
      imports: [NotificationPreferencesPanel],
      providers: [
        {
          provide: NotificationPreferencesStore,
          useValue: {
            preferences: () => null,
            loading: () => false,
            error: () => null,
            closePanel: vi.fn(),
            save: vi.fn(),
          },
        },
        { provide: ChannelStore, useValue: { peerCandidates: () => [], activeWorkspace: () => ({ name: 'Acme' }) } },
        { provide: AuthService, useValue: { profile: () => ({ id: 'u-alice' }) } },
        { provide: LocaleService, useValue: { locale: () => 'pt-BR', apply: vi.fn() } },
        {
          provide: PushNotificationService,
          useValue: {
            devices: () => [],
            refreshDevices: vi.fn().mockResolvedValue(undefined),
            removeDevice: vi.fn(),
          },
        },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(NotificationPreferencesPanel);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('form').length).toBe(0);
    expect(fixture.nativeElement.querySelector('vc-button')).not.toBeNull();
  });

  it('keeps language, notifications and devices in one panel', async () => {
    await TestBed.configureTestingModule({
      imports: [NotificationPreferencesPanel],
      providers: [
        {
          provide: NotificationPreferencesStore,
          useValue: {
            preferences: () => null,
            loading: () => false,
            error: () => null,
            closePanel: vi.fn(),
            save: vi.fn(),
          },
        },
        { provide: ChannelStore, useValue: { peerCandidates: () => [], activeWorkspace: () => ({ name: 'Acme' }) } },
        { provide: AuthService, useValue: { profile: () => ({ id: 'u-alice', name: 'Alice' }) } },
        { provide: LocaleService, useValue: { locale: () => 'en', apply: vi.fn() } },
        {
          provide: PushNotificationService,
          useValue: {
            devices: () => [],
            refreshDevices: vi.fn().mockResolvedValue(undefined),
            removeDevice: vi.fn(),
          },
        },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(NotificationPreferencesPanel);
    fixture.detectChanges();
    await fixture.whenStable();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('[data-testid="prefs-panel"]')).not.toBeNull();
    expect(host.querySelector('[data-testid="locale-select"]')?.querySelector('option[value="en"]')).not.toBeNull();
    expect(host.querySelector('input[name="prefs-theme"]')).toBeNull();
    expect(host.querySelector('input[name="prefs-density"]')).toBeNull();
    expect(host.querySelector('[data-testid="notif-panel"]')).not.toBeNull();
    expect(host.querySelector('[data-testid="push-devices"]')).not.toBeNull();
    expect(host.textContent).toContain('Alice');
    expect(host.textContent).toContain('Acme');
  });
});
