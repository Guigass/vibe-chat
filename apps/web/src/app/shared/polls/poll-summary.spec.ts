import { describe, expect, it } from 'vitest';
import { pollIsTie, pollLeaderIndexes, pollOptionPercent } from './poll-summary';

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
});
