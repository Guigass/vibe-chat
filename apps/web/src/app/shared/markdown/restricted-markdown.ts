export type MarkdownInline =
  | { kind: 'text'; text: string }
  | { kind: 'strong'; children: MarkdownInline[] }
  | { kind: 'em'; children: MarkdownInline[] }
  | { kind: 'del'; children: MarkdownInline[] }
  | { kind: 'code'; text: string }
  | { kind: 'link'; href: string; text: string }
  | { kind: 'mention'; userId?: string; special?: 'here' | 'channel' }
  | { kind: 'br' };

export type MarkdownBlock =
  | { kind: 'paragraph'; inlines: MarkdownInline[] }
  | { kind: 'code'; language: string; text: string }
  | { kind: 'quote'; blocks: MarkdownBlock[] }
  | { kind: 'ul'; items: MarkdownBlock[][] }
  | { kind: 'ol'; items: MarkdownBlock[][] };

export interface MarkdownDocument {
  blocks: MarkdownBlock[];
}

const URL_INLINE_PATTERN = /https?:\/\/[^\s<]+[^\s<.,:;"')\]}]/;

export function parseRestrictedMarkdown(source: string): MarkdownDocument {
  if (!source) {
    return { blocks: [] };
  }

  try {
    const lines = source.replace(/\r\n/g, '\n').split('\n');
    const blocks = parseBlocks(lines, 0).blocks;
    if (blocks.length === 0) {
      return { blocks: [{ kind: 'paragraph', inlines: parseInlines(source) }] };
    }
    return { blocks };
  } catch {
    return { blocks: [{ kind: 'paragraph', inlines: [{ kind: 'text', text: source }] }] };
  }
}

function parseBlocks(lines: string[], start: number): { blocks: MarkdownBlock[]; next: number } {
  const blocks: MarkdownBlock[] = [];
  let index = start;

  while (index < lines.length) {
    const line = lines[index];

    if (line.trim() === '') {
      index += 1;
      continue;
    }

    if (line.startsWith('```')) {
      const language = line.slice(3).trim();
      index += 1;
      const codeLines: string[] = [];
      while (index < lines.length && !lines[index].startsWith('```')) {
        codeLines.push(lines[index]);
        index += 1;
      }
      if (index < lines.length) {
        index += 1;
      }
      blocks.push({ kind: 'code', language, text: codeLines.join('\n') });
      continue;
    }

    if (line.startsWith('>')) {
      const quoteLines: string[] = [];
      while (index < lines.length && lines[index].startsWith('>')) {
        quoteLines.push(lines[index].replace(/^>\s?/, ''));
        index += 1;
      }
      const nested = parseBlocks(quoteLines, 0).blocks;
      blocks.push({ kind: 'quote', blocks: nested });
      continue;
    }

    if (/^[-*]\s+/.test(line)) {
      const items: MarkdownBlock[][] = [];
      while (index < lines.length && /^[-*]\s+/.test(lines[index])) {
        const itemText = lines[index].replace(/^[-*]\s+/, '');
        items.push([{ kind: 'paragraph', inlines: parseInlines(itemText) }]);
        index += 1;
      }
      blocks.push({ kind: 'ul', items });
      continue;
    }

    if (/^\d+\.\s+/.test(line)) {
      const items: MarkdownBlock[][] = [];
      while (index < lines.length && /^\d+\.\s+/.test(lines[index])) {
        const itemText = lines[index].replace(/^\d+\.\s+/, '');
        items.push([{ kind: 'paragraph', inlines: parseInlines(itemText) }]);
        index += 1;
      }
      blocks.push({ kind: 'ol', items });
      continue;
    }

    const paragraphLines: string[] = [line];
    index += 1;
    while (
      index < lines.length &&
      lines[index].trim() !== '' &&
      !lines[index].startsWith('```') &&
      !lines[index].startsWith('>') &&
      !/^[-*]\s+/.test(lines[index]) &&
      !/^\d+\.\s+/.test(lines[index])
    ) {
      paragraphLines.push(lines[index]);
      index += 1;
    }

    const inlines: MarkdownInline[] = [];
    paragraphLines.forEach((paragraphLine, lineIndex) => {
      if (lineIndex > 0) {
        inlines.push({ kind: 'br' });
      }
      inlines.push(...parseInlines(paragraphLine));
    });
    blocks.push({ kind: 'paragraph', inlines });
  }

  return { blocks, next: index };
}

function parseInlines(text: string): MarkdownInline[] {
  if (!text) {
    return [];
  }

  const nodes: MarkdownInline[] = [];
  let cursor = 0;

  while (cursor < text.length) {
    const slice = text.slice(cursor);

    const codeMatch = slice.match(/^`([^`\n]+)`/);
    if (codeMatch) {
      nodes.push({ kind: 'code', text: codeMatch[1] });
      cursor += codeMatch[0].length;
      continue;
    }

    const mentionMatch = slice.match(/^<@([0-9a-fA-F-]{36}|here|channel)>/i);
    if (mentionMatch) {
      const value = mentionMatch[1].toLowerCase();
      if (value === 'here') {
        nodes.push({ kind: 'mention', special: 'here' });
      } else if (value === 'channel') {
        nodes.push({ kind: 'mention', special: 'channel' });
      } else {
        nodes.push({ kind: 'mention', userId: mentionMatch[1] });
      }
      cursor += mentionMatch[0].length;
      continue;
    }

    const boldMatch = slice.match(/^\*\*([^*\n]+)\*\*/);
    if (boldMatch) {
      nodes.push({ kind: 'strong', children: parseInlines(boldMatch[1]) });
      cursor += boldMatch[0].length;
      continue;
    }

    const strikeMatch = slice.match(/^~~([^~\n]+)~~/);
    if (strikeMatch) {
      nodes.push({ kind: 'del', children: parseInlines(strikeMatch[1]) });
      cursor += strikeMatch[0].length;
      continue;
    }

    const italicMatch = slice.match(/^\*([^*\n]+)\*/);
    if (italicMatch) {
      nodes.push({ kind: 'em', children: parseInlines(italicMatch[1]) });
      cursor += italicMatch[0].length;
      continue;
    }

    const urlMatch = slice.match(URL_INLINE_PATTERN);
    if (urlMatch && urlMatch.index === 0) {
      const href = urlMatch[0];
      nodes.push({ kind: 'link', href, text: href });
      cursor += href.length;
      continue;
    }

    const nextSpecial = findNextSpecialIndex(slice);
    const plainEnd = nextSpecial === -1 ? slice.length : nextSpecial;
    if (plainEnd > 0) {
      nodes.push({ kind: 'text', text: slice.slice(0, plainEnd) });
      cursor += plainEnd;
      continue;
    }

    nodes.push({ kind: 'text', text: slice[0] });
    cursor += 1;
  }

  return mergeTextNodes(nodes);
}

function findNextSpecialIndex(text: string): number {
  const markers = ['`', '**', '~~', '<@', 'http://', 'https://'];
  let earliest = -1;
  for (const marker of markers) {
    const index = text.indexOf(marker);
    if (index === -1) continue;
    if (earliest === -1 || index < earliest) {
      earliest = index;
    }
  }
  const starIndex = text.indexOf('*');
  if (starIndex !== -1 && !text.startsWith('**', starIndex)) {
    if (earliest === -1 || starIndex < earliest) {
      earliest = starIndex;
    }
  }
  return earliest;
}

function mergeTextNodes(nodes: MarkdownInline[]): MarkdownInline[] {
  const merged: MarkdownInline[] = [];
  for (const node of nodes) {
    const last = merged[merged.length - 1];
    if (node.kind === 'text' && last?.kind === 'text') {
      last.text += node.text;
    } else {
      merged.push(node);
    }
  }
  return merged;
}

export function highlightCode(language: string, code: string): { text: string; className?: string }[] {
  const lang = language.trim().toLowerCase();
  if (!lang) {
    return [{ text: code }];
  }

  if (lang === 'sql') {
    return tokenizeWithKeywords(
      code,
      /\b(SELECT|FROM|WHERE|JOIN|LEFT|RIGHT|INNER|OUTER|ON|AND|OR|NOT|NULL|AS|ORDER|BY|GROUP|HAVING|LIMIT|INSERT|INTO|VALUES|UPDATE|SET|DELETE|CREATE|TABLE|INDEX|PRIMARY|KEY|FOREIGN|REFERENCES|UNION|ALL|DISTINCT|COUNT|SUM|AVG|MIN|MAX|CASE|WHEN|THEN|ELSE|END|EXISTS|IN|LIKE|BETWEEN|IS|TRUE|FALSE|ASC|DESC)\b/gi,
      'sql-kw',
    );
  }

  if (lang === 'json') {
    return tokenizeWithPattern(code, /("(?:\\.|[^"\\])*")|\b(true|false|null)\b|-?\d+(?:\.\d+)?/g, (match) => {
      if (match.startsWith('"')) return 'json-str';
      if (match === 'true' || match === 'false' || match === 'null') return 'json-lit';
      return 'json-num';
    });
  }

  if (lang === 'js' || lang === 'javascript' || lang === 'ts' || lang === 'typescript') {
    return tokenizeWithKeywords(
      code,
      /\b(const|let|var|function|return|if|else|for|while|class|extends|import|export|from|async|await|new|this|typeof|instanceof|try|catch|finally|throw|switch|case|break|continue|default|null|undefined|true|false)\b/g,
      'js-kw',
    );
  }

  return [{ text: code }];
}

function tokenizeWithKeywords(
  code: string,
  pattern: RegExp,
  className: string,
): { text: string; className?: string }[] {
  return tokenizeWithPattern(code, pattern, () => className);
}

function tokenizeWithPattern(
  code: string,
  pattern: RegExp,
  classify: (match: string) => string | undefined,
): { text: string; className?: string }[] {
  const tokens: { text: string; className?: string }[] = [];
  let lastIndex = 0;
  pattern.lastIndex = 0;
  let match = pattern.exec(code);
  while (match) {
    if (match.index > lastIndex) {
      tokens.push({ text: code.slice(lastIndex, match.index) });
    }
    const className = classify(match[0]);
    tokens.push({ text: match[0], className });
    lastIndex = match.index + match[0].length;
    match = pattern.exec(code);
  }
  if (lastIndex < code.length) {
    tokens.push({ text: code.slice(lastIndex) });
  }
  return tokens.length ? tokens : [{ text: code }];
}
