import { PollOptionSummary, PollSummary, PollVoter } from '../models/chat.models';

export function pollOptionPercent(voteCount: number, totalVotes: number): number {
  if (totalVotes <= 0 || voteCount <= 0) return 0;
  return Math.round((voteCount * 100) / totalVotes);
}

export function pollLeaderIndexes(options: Pick<PollOptionSummary, 'voteCount'>[]): number[] {
  if (!options.length) return [];
  const max = Math.max(...options.map((option) => option.voteCount));
  if (max <= 0) return [];
  return options
    .map((option, index) => (option.voteCount === max ? index : -1))
    .filter((index) => index >= 0);
}

export function pollIsTie(options: Pick<PollOptionSummary, 'voteCount'>[]): boolean {
  return pollLeaderIndexes(options).length > 1;
}

export function pollIsClosed(poll: Pick<PollSummary, 'closedAt'>): boolean {
  return !!poll.closedAt;
}

export function pollMineKey(poll: Pick<PollSummary, 'options'>): string {
  return poll.options
    .filter((option) => option.votedByMe)
    .map((option) => option.id)
    .sort()
    .join(',');
}

/**
 * Remote snapshots cannot change *my* selection. A late PollChanged from the
 * previous vote has the same totalVotes and would otherwise revert the card.
 */
export function mergeRemotePoll(
  current?: PollSummary | null,
  incoming?: PollSummary | null,
): PollSummary | null {
  if (!incoming?.options?.length) return current ?? incoming ?? null;
  if (!current?.options?.length) return incoming;
  if (current.closedAt && !incoming.closedAt) return current;
  if ((incoming.totalVotes ?? 0) < (current.totalVotes ?? 0)) return current;
  if (pollMineKey(current) !== pollMineKey(incoming)) return current;
  return incoming;
}

/** Alias used when merging MessageCreated into an existing bubble. */
export function preferRicherPoll(
  current?: PollSummary | null,
  incoming?: PollSummary | null,
): PollSummary | null {
  return mergeRemotePoll(current, incoming);
}

/** HTTP is camelCase; outbox/SignalR may still emit PascalCase PollDto records. */
export function mapPollSummary(raw: unknown): PollSummary | null {
  if (!raw || typeof raw !== 'object') return null;
  const poll = raw as Record<string, unknown>;
  const id = readString(poll, 'id', 'Id');
  const optionsRaw = readValue(poll, 'options', 'Options');
  if (!id || !Array.isArray(optionsRaw)) return null;

  return {
    id,
    messageId: readString(poll, 'messageId', 'MessageId') || id,
    channelId: readString(poll, 'channelId', 'ChannelId') || '',
    question: readString(poll, 'question', 'Question') || '',
    allowMultiple: !!readValue(poll, 'allowMultiple', 'AllowMultiple'),
    anonymous: !!readValue(poll, 'anonymous', 'Anonymous'),
    closesAt: readString(poll, 'closesAt', 'ClosesAt') || null,
    closedAt: readString(poll, 'closedAt', 'ClosedAt') || null,
    totalVotes: Number(readValue(poll, 'totalVotes', 'TotalVotes') ?? 0) || 0,
    canVote: !!readValue(poll, 'canVote', 'CanVote'),
    options: optionsRaw
      .map((item) => mapPollOption(item))
      .filter((item): item is PollOptionSummary => item !== null)
      .sort((a, b) => a.position - b.position),
  };
}

function mapPollOption(raw: unknown): PollOptionSummary | null {
  if (!raw || typeof raw !== 'object') return null;
  const option = raw as Record<string, unknown>;
  const id = readString(option, 'id', 'Id');
  const text = readString(option, 'text', 'Text');
  if (!id || !text) return null;
  const votersRaw = readValue(option, 'voters', 'Voters');
  return {
    id,
    text,
    position: Number(readValue(option, 'position', 'Position') ?? 0) || 0,
    voteCount: Number(readValue(option, 'voteCount', 'VoteCount') ?? 0) || 0,
    percent: Number(readValue(option, 'percent', 'Percent') ?? 0) || 0,
    votedByMe: !!readValue(option, 'votedByMe', 'VotedByMe'),
    voters: Array.isArray(votersRaw)
      ? votersRaw
          .map((item) => mapPollVoter(item))
          .filter((item): item is PollVoter => item !== null)
      : undefined,
  };
}

function mapPollVoter(raw: unknown): PollVoter | null {
  if (!raw || typeof raw !== 'object') return null;
  const voter = raw as Record<string, unknown>;
  const userId = readString(voter, 'userId', 'UserId');
  if (!userId) return null;
  return {
    userId,
    displayName: readString(voter, 'displayName', 'DisplayName') || userId,
  };
}

function readValue(source: Record<string, unknown>, camel: string, pascal: string): unknown {
  return source[camel] ?? source[pascal];
}

function readString(source: Record<string, unknown>, camel: string, pascal: string): string {
  const value = readValue(source, camel, pascal);
  return value == null ? '' : String(value);
}
