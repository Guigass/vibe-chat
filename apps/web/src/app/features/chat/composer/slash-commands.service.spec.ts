/** @vitest-environment jsdom */
import '@angular/compiler';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiService } from '../../../core/api/api.service';
import { ChannelStore } from '../../../core/services/channel.store';
import { MessageStore } from '../../../core/services/message.store';
import { SlashCommandsService } from './slash-commands.service';

describe('SlashCommandsService (B-087)', () => {
  const api = {
    getCommands: vi.fn(),
    updateChannelTopic: vi.fn(),
    inviteMember: vi.fn(),
    summarizeChannel: vi.fn(),
  };

  const channels = {
    isDemo: vi.fn(() => false),
    activeWorkspace: vi.fn(() => ({ id: 'ws-1', name: 'Demo', slug: 'demo' })),
    activeChannel: vi.fn(() => ({
      id: 'ch-1',
      workspaceId: 'ws-1',
      name: 'geral',
      unreadCount: 0,
      isDirect: false,
    })),
    members: vi.fn(() => [
      { userId: 'u-bob', displayName: 'Bob', email: 'bob@vibechat.local', role: 'Member' },
    ]),
    openDirectMessage: vi.fn(async () => ({ id: 'dm-1' })),
    patchChannel: vi.fn(),
  };

  const messages = {
    messages: signal([
      {
        id: 'm-1',
        channelId: 'ch-1',
        body: 'oi',
        authorUserId: 'me',
        authorName: 'Me',
        createdAt: new Date().toISOString(),
        seq: 1,
        mine: true,
      },
    ]),
    remove: vi.fn(async () => undefined),
  };

  beforeEach(() => {
    vi.clearAllMocks();
    api.getCommands.mockResolvedValue([
      { name: 'ajuda', description: 'Lista', usage: '/ajuda' },
      { name: 'dm', description: 'DM', usage: '/dm @pessoa' },
      { name: 'resumir', description: 'IA', usage: '/resumir' },
    ]);
    TestBed.configureTestingModule({
      providers: [
        SlashCommandsService,
        { provide: ApiService, useValue: api },
        { provide: ChannelStore, useValue: channels },
        { provide: MessageStore, useValue: messages },
      ],
    });
  });

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('rejects unknown commands and keeps draft', async () => {
    const svc = TestBed.inject(SlashCommandsService);
    const result = await svc.execute('/xpto');
    expect(result.ok).toBe(false);
    expect(result.clearDraft).toBe(false);
    expect(result.notice?.kind).toBe('error');
    expect(result.notice?.text).toContain('/xpto');
  });

  it('lists ajuda from server catalog', async () => {
    const svc = TestBed.inject(SlashCommandsService);
    const result = await svc.execute('/ajuda');
    expect(result.ok).toBe(true);
    expect(result.clearDraft).toBe(true);
    expect(result.notice?.kind).toBe('help');
    expect(result.notice?.lines?.some((line) => line.includes('/dm'))).toBe(true);
  });

  it('explains when IA is disabled instead of raw 503', async () => {
    const err = new Error(JSON.stringify({ error: 'AiDisabled' })) as Error & { status: number };
    err.status = 503;
    api.summarizeChannel.mockRejectedValue(err);

    const svc = TestBed.inject(SlashCommandsService);
    const result = await svc.execute('/resumir');
    expect(result.ok).toBe(false);
    expect(result.notice?.text).toMatch(/IA está desligada/i);
  });

  it('opens DM by member display name', async () => {
    const svc = TestBed.inject(SlashCommandsService);
    const result = await svc.execute('/dm @Bob');
    expect(result.ok).toBe(true);
    expect(channels.openDirectMessage).toHaveBeenCalledWith('u-bob');
  });
});
