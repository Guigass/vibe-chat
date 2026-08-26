import { PollOptionSummary, PollSummary } from '../models/chat.models';

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
