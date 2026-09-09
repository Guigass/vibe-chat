import { describe, expect, it } from 'vitest';
import { mapPollSummary, pollIsTie, pollLeaderIndexes, pollOptionPercent, preferRicherPoll } from './poll-summary';

describe('poll-summary', () => {
  it('computes percent over emitted votes', () => {
    expect(pollOptionPercent(1, 4)).toBe(25);
    expect(pollOptionPercent(0, 0)).toBe(0);
  });

  it('marks a tie without a single winner', () => {
    const options = [{ voteCount: 2 }, { voteCount: 2 }, { voteCount: 1 }];
    expect(pollLeaderIndexes(options)).toEqual([0, 1]);
    expect(pollIsTie(options)).toBe(true);
  });

  it('marks a single leader when counts differ', () => {
    const options = [{ voteCount: 3 }, { voteCount: 1 }];
    expect(pollLeaderIndexes(options)).toEqual([0]);
    expect(pollIsTie(options)).toBe(false);
  });

  it('maps camelCase and PascalCase hub payloads', () => {
    const camel = mapPollSummary({
      id: 'p1',
      messageId: 'm1',
      channelId: 'c1',
      question: 'Lanche?',
      allowMultiple: false,
      anonymous: false,
      totalVotes: 1,
      canVote: true,
      options: [{ id: 'o1', text: 'Pizza', position: 0, voteCount: 1, percent: 100, votedByMe: true }],
    });
    expect(camel?.question).toBe('Lanche?');
    expect(camel?.options).toHaveLength(1);

    const pascal = mapPollSummary({
      Id: 'p1',
      MessageId: 'm1',
      ChannelId: 'c1',
      Question: 'Lanche?',
      AllowMultiple: false,
      Anonymous: false,
      TotalVotes: 0,
      CanVote: true,
      Options: [
        { Id: 'o1', Text: 'Pizza', Position: 0, VoteCount: 0, Percent: 0, VotedByMe: false },
        { Id: 'o2', Text: 'Hambúrguer', Position: 1, VoteCount: 0, Percent: 0, VotedByMe: false },
      ],
    });
    expect(pascal?.question).toBe('Lanche?');
    expect(pascal?.options.map((option) => option.text)).toEqual(['Pizza', 'Hambúrguer']);
    expect(pascal?.totalVotes).toBe(0);
  });

  it('returns null when options are missing', () => {
    expect(mapPollSummary({ id: 'p1', Question: 'x' })).toBeNull();
  });

  it('keeps the voted snapshot when a late create echo has fewer votes', () => {
    const voted = mapPollSummary({
      id: 'p1',
      messageId: 'm1',
      channelId: 'c1',
      question: 'Lanche?',
      totalVotes: 1,
      canVote: true,
      options: [{ id: 'o1', text: 'Pizza', position: 0, voteCount: 1, percent: 100, votedByMe: true }],
    });
    const created = mapPollSummary({
      id: 'p1',
      messageId: 'm1',
      channelId: 'c1',
      question: 'Lanche?',
      totalVotes: 0,
      canVote: true,
      options: [{ id: 'o1', text: 'Pizza', position: 0, voteCount: 0, percent: 0, votedByMe: false }],
    });
    expect(preferRicherPoll(voted, created)?.totalVotes).toBe(1);
  });

  it('keeps the latest local choice when a late PollChanged echoes the previous vote', () => {
    const pizza = mapPollSummary({
      id: 'p1',
      messageId: 'm1',
      channelId: 'c1',
      question: 'Lanche?',
      totalVotes: 1,
      canVote: true,
      options: [
        { id: 'o1', text: 'Pizza', position: 0, voteCount: 1, percent: 100, votedByMe: true },
        { id: 'o2', text: 'Hambúrguer', position: 1, voteCount: 0, percent: 0, votedByMe: false },
      ],
    });
    const burger = mapPollSummary({
      id: 'p1',
      messageId: 'm1',
      channelId: 'c1',
      question: 'Lanche?',
      totalVotes: 1,
      canVote: true,
      options: [
        { id: 'o1', text: 'Pizza', position: 0, voteCount: 0, percent: 0, votedByMe: false },
        { id: 'o2', text: 'Hambúrguer', position: 1, voteCount: 1, percent: 100, votedByMe: true },
      ],
    });
    expect(preferRicherPoll(burger, pizza)?.options.find((option) => option.votedByMe)?.text).toBe(
      'Hambúrguer',
    );
  });
});
