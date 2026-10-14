import { describe, expect, it } from 'vitest';
import { Channel } from '../../shared/models/chat.models';
import { landingChannel } from './channel-directory';

function channel(partial: Partial<Channel> & Pick<Channel, 'id' | 'name'>): Channel {
  return {
    workspaceId: 'ws',
    unreadCount: 0,
    ...partial,
  };
}

describe('landingChannel', () => {
  it('keeps the first stream channel when nothing is a topic forum', () => {
    const avisos = channel({ id: 'avisos', name: 'avisos' });
    const geral = channel({ id: 'geral', name: 'geral' });
    expect(landingChannel([avisos, geral])?.id).toBe('avisos');
  });

  it('opens the stream home when a topics channel sorts first', () => {
    const forum = channel({ id: 'forum', name: 'forum-abc', layout: 'Topics' });
    const geral = channel({ id: 'geral', name: 'geral', layout: 'Stream' });
    const dm = channel({ id: 'dm', name: 'Alice', isDirect: true, layout: 'Stream' });
    expect(landingChannel([forum, dm, geral])?.id).toBe('geral');
  });

  it('opens the topics channel when the workspace has no stream channel', () => {
    const forum = channel({ id: 'forum', name: 'forum-abc', layout: 'Topics' });
    expect(landingChannel([forum])?.id).toBe('forum');
    expect(landingChannel([])).toBeUndefined();
  });
});
