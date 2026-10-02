/** Debounced read-cursor persistence (B-094). Same 1s coalesce as the store used to own. */
export class ReadCursorScheduler {
  private timer: ReturnType<typeof setTimeout> | null = null;
  private pending: { channelId: string; seq: number } | null = null;

  constructor(private readonly persist: (channelId: string, seq: number) => Promise<void>) {}

  schedule(channelId: string, seq: number, allow: boolean): void {
    if (!allow) return;
    if (!channelId || seq <= 0) return;
    if (typeof document !== 'undefined' && !document.hasFocus()) return;

    const pending = this.pending;
    if (pending && pending.channelId === channelId) {
      this.pending = { channelId, seq: Math.max(pending.seq, seq) };
    } else {
      this.pending = { channelId, seq };
    }

    if (this.timer) clearTimeout(this.timer);
    this.timer = setTimeout(() => {
      const next = this.pending;
      this.pending = null;
      this.timer = null;
      if (next) void this.persist(next.channelId, next.seq);
    }, 1000);
  }

  cancelPending(): void {
    if (this.timer) {
      clearTimeout(this.timer);
      this.timer = null;
    }
    this.pending = null;
  }

  flushPending(): void {
    const pending = this.pending;
    if (!pending) return;
    this.cancelPending();
    void this.persist(pending.channelId, pending.seq);
  }
}
