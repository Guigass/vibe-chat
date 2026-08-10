import { describe, expect, it } from 'vitest';
import {
  detectSlashQuery,
  filterSlashCommands,
  insertSlashCommand,
  looksLikeSlashCommand,
  parseSlashCommand,
  SlashCommandDef,
} from './slash-tokens';

const catalog: SlashCommandDef[] = [
  { name: 'dm', description: 'Abre ou cria uma DM', usage: '/dm @pessoa' },
  { name: 'topico', description: 'Altera a descrição do canal', usage: '/topico <texto>' },
  { name: 'convidar', description: 'Convida alguém', usage: '/convidar <email>' },
  { name: 'ajuda', description: 'Lista os comandos', usage: '/ajuda' },
];

describe('slash tokens', () => {
  it('detects slash query only at message start before space', () => {
    expect(detectSlashQuery('/aju', 4)?.query).toBe('aju');
    expect(detectSlashQuery('/ajuda', 6)?.query).toBe('ajuda');
    expect(detectSlashQuery('/ajuda agora', 7)).toBeNull();
    expect(detectSlashQuery('oi /ajuda', 9)).toBeNull();
  });

  it('parses command name and args', () => {
    expect(parseSlashCommand('/DM @bob')).toEqual({ name: 'dm', argsRaw: '@bob' });
    expect(parseSlashCommand('/topico hello world')).toEqual({
      name: 'topico',
      argsRaw: 'hello world',
    });
    expect(parseSlashCommand('/ajuda')).toEqual({ name: 'ajuda', argsRaw: '' });
    expect(parseSlashCommand('not a command')).toBeNull();
  });

  it('filters commands by prefix', () => {
    const items = filterSlashCommands(catalog, 'convi');
    expect(items.map((c) => c.name)).toEqual(['convidar']);
  });

  it('inserts completed command with trailing space', () => {
    const result = insertSlashCommand('/aju', 'ajuda');
    expect(result.value).toBe('/ajuda ');
  });

  it('detects slash-looking drafts so they are not sent as messages', () => {
    expect(looksLikeSlashCommand('/xpto')).toBe(true);
    expect(looksLikeSlashCommand('  /ajuda')).toBe(true);
    expect(looksLikeSlashCommand('hello')).toBe(false);
  });
});
