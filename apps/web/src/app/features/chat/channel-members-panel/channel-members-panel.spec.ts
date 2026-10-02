import { TestBed } from '@angular/core/testing';
import { describe, expect, it, vi } from 'vitest';
import { AuthService } from '../../../core/auth/auth.service';
import { ApiService } from '../../../core/api/api.service';
import { ChannelStore } from '../../../core/services/channel.store';
import { MessageStore } from '../../../core/services/message.store';
import { ChannelMembersPanel } from './channel-members-panel';

describe('ChannelMembersPanel', () => {
  it('opens a direct message from another member', async () => {
    const openDirectMessage = vi.fn().mockResolvedValue({ id: 'dm-bob' });
    const loadChannel = vi.fn().mockResolvedValue(undefined);
    const getChannelRoster = vi.fn().mockResolvedValue({
      items: [
        { userId: 'u-alice', displayName: 'Alice', email: 'alice@vibechat.local', isGuest: false, presence: 'online' },
        { userId: 'u-bob', displayName: 'Bob', email: 'bob@vibechat.local', isGuest: false, presence: 'away' },
      ],
      nextCursor: null,
      total: 2,
      canManage: true,
    });

    await TestBed.configureTestingModule({
      imports: [ChannelMembersPanel],
      providers: [
        {
          provide: ApiService,
          useValue: { getChannelRoster, addChannelMember: vi.fn(), removeChannelMember: vi.fn(), leaveChannel: vi.fn() },
        },
        {
          provide: ChannelStore,
          useValue: {
            activeWorkspace: () => ({ id: 'ws-1' }),
            activeChannel: () => ({ id: 'ch-1', isPrivate: true, type: 'private' }),
            isDemo: () => false,
            members: () => [],
            openDirectMessage,
          },
        },
        {
          provide: AuthService,
          useValue: {
            profile: () => ({ id: 'u-alice', name: 'Alice' }),
            isOfflineDemo: () => false,
          },
        },
        { provide: MessageStore, useValue: { loadChannel } },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ChannelMembersPanel);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const directs = fixture.nativeElement.querySelectorAll('[data-testid="channel-member-direct"]');
    expect(directs.length).toBe(1);
    directs[0].click();
    await fixture.whenStable();

    expect(openDirectMessage).toHaveBeenCalledWith('u-bob');
    expect(loadChannel).toHaveBeenCalledWith('dm-bob');
  });
});
