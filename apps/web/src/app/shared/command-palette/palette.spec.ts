/** @vitest-environment jsdom */
import { describe, expect, it } from 'vitest';
import {
  cycleChannel,
  flattenPaletteItems,
  fuzzyScore,
  isEditableTarget,
  matchGlobalShortcut,
  rankPaletteItems,
  type PaletteItem,
} from './palette';

function item(
  id: string,
  kind: PaletteItem['kind'],
  label: string,
  extra: Partial<PaletteItem> = {},
): PaletteItem {
  return {
    id,
    kind,
    label,
    keywords: extra.keywords ?? [label],
    action: extra.action ?? { type: 'saved' },
    ...extra,
  };
}

describe('command palette scoring (B-099)', () => {
  it('scores “ger” as a prefix of #geral', () => {
    expect(fuzzyScore('ger', 'geral')).toBe(80);
    expect(fuzzyScore('ger', '#geral')).toBe(60);
    expect(fuzzyScore('ger', 'incidentes')).toBe(0);
  });

  it('groups by type and lifts recents when the query is empty', () => {
    const items = [
      item('ch-geral', 'channel', '#geral'),
      item('ch-ops', 'channel', '#incidentes'),
      item('u-bob', 'person', 'Bob'),
      item('act-theme', 'action', 'Alternar tema', { shortcut: '—' }),
    ];
    const empty = rankPaletteItems(items, '', ['u-bob', 'ch-geral']);
    expect(empty.map((g) => g.kind)).toEqual(['recent', 'channel', 'person', 'action']);
    expect(empty[0].items.map((row) => row.id)).toEqual(['u-bob', 'ch-geral']);

    const filtered = rankPaletteItems(items, 'ger', []);
    expect(filtered).toHaveLength(1);
    expect(filtered[0].kind).toBe('channel');
    expect(filtered[0].items[0].label).toBe('#geral');
  });

  it('flattens groups without duplicating recents', () => {
    const groups = rankPaletteItems(
      [item('ch-geral', 'channel', '#geral'), item('act-theme', 'action', 'Tema')],
      '',
      ['ch-geral'],
    );
    expect(flattenPaletteItems(groups).map((row) => row.id)).toEqual(['ch-geral', 'act-theme']);
  });
});

describe('command palette shortcuts (B-099)', () => {
  it('maps the documented modifier shortcuts even inside a text field', () => {
    expect(matchGlobalShortcut(mod('k'), true)).toBe('palette');
    expect(matchGlobalShortcut(mod('k', { meta: true }), true)).toBe('palette');
    expect(matchGlobalShortcut(mod('f', { shift: true }), true)).toBe('search');
    expect(matchGlobalShortcut(mod('m', { shift: true }), false)).toBe('mentions');
    expect(matchGlobalShortcut(key('Escape', { shift: true }), true)).toBe('mark-read');
  });

  it('blocks character and Alt channel shortcuts while typing', () => {
    expect(matchGlobalShortcut(key('?'), true)).toBeNull();
    expect(matchGlobalShortcut(key('?'), false)).toBe('shortcuts');
    expect(matchGlobalShortcut(key('ArrowDown', { alt: true }), true)).toBeNull();
    expect(matchGlobalShortcut(key('ArrowDown', { alt: true }), false)).toBe('channel-next');
    expect(matchGlobalShortcut(key('ArrowUp', { alt: true, shift: true }), false)).toBe(
      'unread-prev',
    );
  });

  it('treats input/textarea/select as editable targets', () => {
    const input = document.createElement('input');
    const textarea = document.createElement('textarea');
    const div = document.createElement('div');
    expect(isEditableTarget(input)).toBe(true);
    expect(isEditableTarget(textarea)).toBe(true);
    expect(isEditableTarget(div)).toBe(false);
  });
});

describe('channel cycling (B-099)', () => {
  const channels = [
    { id: 'a', unreadCount: 0 },
    { id: 'b', unreadCount: 2, mentionCount: 1 },
    { id: 'c', unreadCount: 0 },
  ];

  it('wraps to the next / previous channel', () => {
    expect(cycleChannel(channels, 'a', 1, 'all')?.id).toBe('b');
    expect(cycleChannel(channels, 'c', 1, 'all')?.id).toBe('a');
    expect(cycleChannel(channels, 'a', -1, 'all')?.id).toBe('c');
  });

  it('skips read channels when filtering unread or mention', () => {
    expect(cycleChannel(channels, 'a', 1, 'unread')?.id).toBe('b');
    expect(cycleChannel(channels, 'b', 1, 'unread')?.id).toBe('b');
    expect(cycleChannel(channels, null, 1, 'mention')?.id).toBe('b');
    expect(cycleChannel(channels, 'a', 1, 'mention')?.id).toBe('b');
  });
});

function key(
  name: string,
  opts: { alt?: boolean; shift?: boolean; ctrl?: boolean; meta?: boolean } = {},
): KeyboardEvent {
  return {
    key: name,
    altKey: !!opts.alt,
    shiftKey: !!opts.shift,
    ctrlKey: !!opts.ctrl,
    metaKey: !!opts.meta,
  } as KeyboardEvent;
}

function mod(letter: string, opts: { shift?: boolean; meta?: boolean } = {}): KeyboardEvent {
  return key(letter, { ctrl: !opts.meta, meta: opts.meta, shift: opts.shift });
}
