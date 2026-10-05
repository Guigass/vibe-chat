import { describe, expect, it } from 'vitest';
import { coerceHubPayload, mapAnnouncementPayload, mapMessageCreated } from './chat-hub-payload';

describe('chat-hub payload mapping', () => {
  it('parses a JSON string payload and rejects invalid JSON', () => {
    expect(coerceHubPayload<{ channelId: string }>('{"channelId":"c1"}')).toEqual({ channelId: 'c1' });
    expect(coerceHubPayload('not-json')).toBeNull();
    expect(coerceHubPayload(null)).toBeNull();
  });

  it('maps MessageCreated and reconciles clientMessageId', () => {
    const message = mapMessageCreated(
      {
        messageId: 'm1',
        clientMessageId: 'client-1',
        channelId: 'c1',
        authorId: 'U-ALICE',
        authorName: 'Alice',
        body: 'oi',
        sequence: 7,
        mentionedUserIds: ['u-bob'],
      },
      'u-alice',
    );

    expect(message).toMatchObject({
      id: 'm1',
      clientMessageId: 'client-1',
      channelId: 'c1',
      seq: 7,
      mine: true,
      mentionsMe: false,
      status: 'persisted',
    });
    expect(mapMessageCreated({ channelId: 'c1' }, 'u-alice')).toBeNull();
  });

  it('maps an announcement card and ignores an empty payload', () => {
    expect(
      mapAnnouncementPayload({
        messageId: 'm1',
        requiresAcknowledgement: true,
        acknowledgementCount: 2,
      }),
    ).toMatchObject({
      messageId: 'm1',
      requiresAcknowledgement: true,
      acknowledgementCount: 2,
      canAcknowledge: false,
    });
    expect(mapAnnouncementPayload(null)).toBeNull();
    expect(mapAnnouncementPayload({})).toBeNull();
  });
});
