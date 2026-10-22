import { ComponentFixture, TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiService } from '../../../core/api/api.service';
import { ui } from '../../../core/i18n/strings';
import { ChannelStore } from '../../../core/services/channel.store';
import { ChatMessage, MessagePluginCard } from '../../models/chat.models';
import { PluginActionBar } from './plugin-action-bar';

const message = {
  id: 'm1',
  channelId: 'c1',
  conversationId: 'c1',
  authorUserId: 'bot',
  authorName: 'Action Ack',
  body: 'ack',
  createdAt: '2026-10-22T12:00:00Z',
  status: 'persisted',
  mine: false,
} as ChatMessage;

const card: MessagePluginCard = {
  pluginId: 'action-ack',
  pluginName: 'Action Ack',
  expiresAt: '2099-01-01T00:00:00Z',
  actions: ['action-ack/ack'],
};

describe('PluginActionBar (B-111)', () => {
  let fixture: ComponentFixture<PluginActionBar>;
  const invokePluginAction = vi.fn().mockResolvedValue({ action: 'action-ack/ack', status: 'Accepted', idempotent: false });

  beforeEach(async () => {
    invokePluginAction.mockClear();
    await TestBed.configureTestingModule({
      imports: [PluginActionBar],
      providers: [
        { provide: ApiService, useValue: { invokePluginAction } },
        { provide: ChannelStore, useValue: { activeWorkspace: () => ({ id: 'ws-1' }) } },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(PluginActionBar);
    fixture.componentRef.setInput('message', message);
    fixture.componentRef.setInput('card', card);
    fixture.detectChanges();
  });

  it('shows the plugin name and confirms once with a stable key', async () => {
    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('Action Ack');
    expect(host.textContent).toContain(ui.pluginActionAck);
    const button = host.querySelector('button') as HTMLButtonElement;
    button.click();
    button.click();
    await fixture.whenStable();
    expect(invokePluginAction).toHaveBeenCalledTimes(1);
    const first = invokePluginAction.mock.calls[0][3].idempotencyKey as string;
    expect(first.length).toBeGreaterThan(8);
  });
});
