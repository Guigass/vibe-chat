import { MessageAnnouncer, MessageAnnouncementTracker } from './message-announcer';
import { ChatMessage } from '../models/chat.models';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it, vi } from 'vitest';

const messages = (...seqs: number[]) => seqs.map((seq) => ({ seq }) as ChatMessage);

describe('MessageAnnouncementTracker', () => {
  it('silences hydration, history prepend, edits and duplicate deliveries', () => {
    const tracker = new MessageAnnouncementTracker();
    expect(tracker.update('a', [], true)).toBe(false);
    expect(tracker.update('a', messages(10, 11), false)).toBe(false);
    expect(tracker.update('a', messages(1, 10, 11), false)).toBe(false);
    expect(tracker.update('a', messages(1, 10, 11), false)).toBe(false);
    expect(tracker.update('a', messages(10, 11, 12), false)).toBe(true);
    expect(tracker.update('a', messages(10, 11, 12), false)).toBe(false);
  });

  it('resets on conversation switch and announces arrivals in an empty conversation', () => {
    const tracker = new MessageAnnouncementTracker();
    expect(tracker.update('a', messages(99), false)).toBe(false);
    expect(tracker.update('b', [], false)).toBe(false);
    expect(tracker.update('b', messages(1), false)).toBe(true);
    expect(tracker.update('a', messages(99, 100), false)).toBe(false);
  });
});

describe('MessageAnnouncer live region', () => {
  it('keeps a pending announcement through an echo, but cancels it on conversation switch', async () => {
    await TestBed.configureTestingModule({ imports: [MessageAnnouncer] }).compileComponents();
    vi.useFakeTimers();
    const fixture = TestBed.createComponent(MessageAnnouncer);
    try {
      fixture.componentRef.setInput('scope', 'a');
      fixture.componentRef.setInput('messages', messages(1));
      fixture.detectChanges();
      fixture.componentRef.setInput('messages', messages(1, 2));
      fixture.detectChanges();
      fixture.componentRef.setInput('messages', messages(1, 2));
      fixture.detectChanges();
      vi.advanceTimersByTime(200);
      fixture.detectChanges();
      expect(fixture.nativeElement.textContent.trim()).not.toBe('');
      fixture.componentRef.setInput('messages', messages(1, 2, 3));
      fixture.detectChanges();
      fixture.componentRef.setInput('scope', 'b');
      fixture.componentRef.setInput('messages', messages(50));
      fixture.detectChanges();
      vi.advanceTimersByTime(200);
      fixture.detectChanges();
      expect(fixture.nativeElement.textContent.trim()).toBe('');
    } finally {
      fixture.destroy();
      vi.useRealTimers();
    }
  });
});
