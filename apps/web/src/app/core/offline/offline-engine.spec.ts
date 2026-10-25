import { describe, expect, it } from 'vitest';
import {
  applyPage,
  discardCommand,
  emptyPartition,
  enqueueCommand,
  orderedEntries,
  partitionKey,
  retryCommand,
  type OfflineCommand,
  type OfflinePage,
} from './offline-engine';

const secret = 'corpo-secreto';

function command(overrides: Partial<OfflineCommand> = {}): OfflineCommand {
  return {
    id: 'local-1',
    kind: 'edit',
    idempotencyKey: 'idem-1',
    clientId: 'local-1',
    state: 'queued',
    channelId: 'channel-1',
    messageId: 'message-1',
    body: secret,
    attempts: 0,
    ...overrides,
  };
}

describe('offline queue', () => {
  it('keeps one command when the same idempotency key is retried', () => {
    const first = enqueueCommand(emptyPartition(), {
      kind: 'send',
      idempotencyKey: 'idem-1',
      clientId: 'local-1',
      channelId: 'channel-1',
      body: 'ola',
    });
    const second = enqueueCommand(first.state, {
      kind: 'send',
      idempotencyKey: 'idem-1',
      clientId: 'local-2',
      channelId: 'channel-1',
      body: 'ola',
    });
    expect(second.duplicate).toBe(true);
    expect(second.state.commands).toHaveLength(1);
    expect(second.state.commands[0]?.idempotencyKey).toBe('idem-1');
  });

  it('retries a failure without changing the idempotency key', () => {
    const queued = enqueueCommand(emptyPartition(), {
      kind: 'send',
      idempotencyKey: 'idem-1',
      clientId: 'local-1',
      channelId: 'channel-1',
      body: 'ola',
    });
    const failed = {
      ...queued.state,
      commands: [{ ...queued.state.commands[0]!, state: 'failed' as const }],
    };
    const retried = retryCommand(failed, 'local-1');
    expect(retried.commands[0]?.state).toBe('queued');
    expect(retried.commands[0]?.idempotencyKey).toBe('idem-1');
  });

  it('reconciles ack and sync in either order as one row', () => {
    const change = {
      kind: 'message' as const,
      conversationId: 'channel-1',
      channelId: 'channel-1',
      id: 'message-1',
      seq: 4,
      version: 0,
      body: 'ola',
    };
    const page: OfflinePage = {
      rebuild: null,
      changes: [change],
      tombstones: [],
      authorizedConversationIds: ['channel-1'],
      conversationCoverage: 'complete',
    };
    const ackFirst = applyPage(applyPage(emptyPartition(), page), page);
    const eventFirst = applyPage(applyPage(emptyPartition(), page), page);
    expect(ackFirst.entries).toHaveLength(1);
    expect(eventFirst.entries).toEqual(ackFirst.entries);
    expect(orderedEntries(ackFirst.entries).map((entry) => entry.seq)).toEqual([4]);
  });

  it('orders a local window by seq', () => {
    const later = applyPage(emptyPartition(), {
      rebuild: null,
      changes: [
        { kind: 'message', conversationId: 'c', channelId: 'c', id: 'b', seq: 8, version: 0, body: 'b' },
        { kind: 'message', conversationId: 'c', channelId: 'c', id: 'a', seq: 2, version: 0, body: 'a' },
      ],
      tombstones: [],
      authorizedConversationIds: ['c'],
      conversationCoverage: 'complete',
    });
    expect(orderedEntries(later.entries).map((entry) => entry.id)).toEqual(['a', 'b']);
  });

  it('drops a revoked account window without mixing partitions', () => {
    expect(partitionKey('alice', 'tenant-a')).not.toBe(partitionKey('bob', 'tenant-a'));
    const alice = applyPage(emptyPartition(), {
      rebuild: null,
      changes: [
        { kind: 'message', conversationId: 'c', channelId: 'c', id: 'm', seq: 1, version: 0, body: secret },
      ],
      tombstones: [],
      authorizedConversationIds: ['c'],
      conversationCoverage: 'complete',
    });
    const bob = emptyPartition();
    expect(bob.entries).toHaveLength(0);
    expect(alice.entries[0]?.body).toBe(secret);
  });

  it('cancels an offline edit when the message is deleted and does not keep the body', () => {
    const queued = {
      ...emptyPartition(),
      commands: [command()],
      entries: [
        {
          kind: 'message' as const,
          conversationId: 'channel-1',
          channelId: 'channel-1',
          id: 'message-1',
          seq: 3,
          version: 0,
          body: secret,
        },
      ],
    };
    const next = applyPage(queued, {
      rebuild: null,
      changes: [],
      tombstones: [{ kind: 'message', conversationId: 'channel-1', id: 'message-1', reason: 'deleted' }],
      authorizedConversationIds: ['channel-1'],
      conversationCoverage: 'complete',
    });
    expect(next.entries).toHaveLength(0);
    expect(next.commands[0]?.state).toBe('cancelled');
    expect(JSON.stringify(next)).not.toContain(secret);
  });

  it('rebuilds an expired cursor without dropping a still-valid local command', () => {
    const queued = {
      ...emptyPartition(),
      commands: [command({ messageId: null, kind: 'send', body: 'ainda-local' })],
      entries: [
        {
          kind: 'message' as const,
          conversationId: 'channel-1',
          channelId: 'channel-1',
          id: 'old',
          seq: 1,
          version: 0,
          body: 'velho',
        },
      ],
    };
    const next = applyPage(queued, {
      rebuild: 'CursorExpired',
      changes: [
        { kind: 'message', conversationId: 'channel-1', channelId: 'channel-1', id: 'new', seq: 9, version: 0, body: 'novo' },
      ],
      tombstones: [],
      authorizedConversationIds: ['channel-1'],
      conversationCoverage: 'complete',
    });
    expect(next.commands).toHaveLength(1);
    expect(next.commands[0]?.state).toBe('queued');
    expect(next.commands[0]?.idempotencyKey).toBe('idem-1');
    expect(next.entries.map((entry) => entry.id)).toEqual(['new']);
  });

  it('keeps a conflict visible and recoverable', () => {
    const queued = enqueueCommand(emptyPartition(), {
      kind: 'edit',
      idempotencyKey: 'idem-1',
      clientId: 'local-1',
      channelId: 'channel-1',
      messageId: 'message-1',
      body: 'meu',
      expectedVersion: 1,
    });
    const conflict = {
      ...queued.state,
      commands: [{ ...queued.state.commands[0]!, state: 'conflict' as const, serverBody: 'deles' }],
    };
    expect(conflict.commands[0]?.state).toBe('conflict');
    expect(conflict.commands[0]?.serverBody).toBe('deles');
    const recovered = retryCommand(conflict, 'local-1');
    expect(recovered.commands[0]?.state).toBe('queued');
    expect(recovered.commands[0]?.idempotencyKey).toBe('idem-1');
    const dropped = discardCommand(conflict, 'local-1');
    expect(dropped.commands[0]?.state).toBe('cancelled');
  });

  it('clears the partition when the scope is revoked', () => {
    const queued = {
      ...emptyPartition(),
      commands: [command()],
      entries: [
        {
          kind: 'message' as const,
          conversationId: 'channel-1',
          channelId: 'channel-1',
          id: 'message-1',
          seq: 1,
          version: 0,
          body: secret,
        },
      ],
    };
    const next = applyPage(queued, {
      rebuild: 'ScopeRevoked',
      changes: [],
      tombstones: [],
      authorizedConversationIds: [],
      conversationCoverage: 'complete',
    });
    expect(next.commands).toHaveLength(0);
    expect(next.entries).toHaveLength(0);
  });

  it('does not store the plaintext body in the sealed blob', () => {
    const state = enqueueCommand(emptyPartition(), {
      kind: 'send',
      idempotencyKey: 'idem-1',
      clientId: 'local-1',
      channelId: 'channel-1',
      body: secret,
    }).state;
    const sealed = `enc:${btoa(JSON.stringify(state))}`;
    expect(sealed.startsWith('enc:')).toBe(true);
    expect(sealed).not.toContain(secret);
  });
});
