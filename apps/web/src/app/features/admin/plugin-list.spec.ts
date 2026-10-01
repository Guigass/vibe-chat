import { describe, expect, it } from 'vitest';
import { InstalledPlugin } from '../../shared/models/chat.models';
import { filterInstalledPlugins } from './plugin-list';

const incoming: InstalledPlugin = {
  id: 'p1',
  pluginId: 'incoming-messages',
  name: 'Incoming Messages API',
  version: '1.0.0',
  capabilities: ['messages.send'],
  enabled: true,
  botId: 'b1',
  allowDms: false,
  channelIds: [],
  tokenConfigured: true,
  tokenLast4: 'ab12',
  installedAt: '2026-10-01T00:00:00Z',
  updatedAt: '2026-10-01T00:00:00Z',
};

const pager: InstalledPlugin = {
  ...incoming,
  id: 'p2',
  pluginId: 'pager',
  name: 'Pager',
  version: '2.0.0',
  capabilities: ['messages.send'],
};

describe('filterInstalledPlugins (B-110)', () => {
  it('returns every plugin when the query is empty', () => {
    expect(filterInstalledPlugins([incoming, pager], '  ')).toEqual([incoming, pager]);
  });

  it('matches name, slug or capability and yields an empty list when nothing matches', () => {
    expect(filterInstalledPlugins([incoming, pager], 'pager').map((item) => item.id)).toEqual(['p2']);
    expect(filterInstalledPlugins([incoming, pager], 'INCOMING')).toEqual([incoming]);
    expect(filterInstalledPlugins([incoming, pager], 'messages.send')).toEqual([incoming, pager]);
    expect(filterInstalledPlugins([incoming, pager], 'loja')).toEqual([]);
  });
});
