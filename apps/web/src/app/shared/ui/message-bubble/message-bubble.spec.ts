import { TestBed } from '@angular/core/testing';
import { describe, expect, it, vi } from 'vitest';
import { MessageBubble } from './message-bubble';
import { ApiService } from '../../../core/api/api.service';
import { ChannelStore } from '../../../core/services/channel.store';
import { ThemeService } from '../../../core/services/theme.service';
import type { ChatMessage } from '../../models/chat.models';

function baseMessage(overrides: Partial<ChatMessage> = {}): ChatMessage {
  return {
    id: 'm1',
    conversationId: 'c1',
    channelId: 'ch1',
    authorUserId: 'u1',
    authorName: 'Alice',
    body: 'olá',
    createdAt: new Date().toISOString(),
    status: 'persisted',
    mine: true,
    ...overrides,
  };
}

describe('MessageBubble (B-163)', () => {
  async function setup(
    message: ChatMessage,
    inputs: Record<string, unknown> = {},
    apiOverrides: Partial<{
      getAttachmentDownload: ReturnType<typeof vi.fn>;
    }> = {},
  ) {
    TestBed.resetTestingModule();
    const getAttachmentDownload =
      apiOverrides.getAttachmentDownload ??
      vi.fn().mockResolvedValue({ downloadUrl: 'https://example.test/f' });
    await TestBed.configureTestingModule({
      imports: [MessageBubble],
      providers: [
        {
          provide: ApiService,
          useValue: {
            getAttachmentDownload,
            getReactionUsers: vi.fn(),
            transcribeAttachment: vi.fn(),
          },
        },
        {
          provide: ChannelStore,
          useValue: {
            mentionLabels: () => ({}),
            activeWorkspace: () => ({ id: 'w1' }),
            isDemo: () => true,
          },
        },
        {
          provide: ThemeService,
          useValue: {
            density: () => 'comfortable' as const,
            theme: () => 'light' as const,
          },
        },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(MessageBubble);
    fixture.componentRef.setInput('message', message);
    for (const [key, value] of Object.entries(inputs)) {
      fixture.componentRef.setInput(key, value);
    }
    fixture.detectChanges();
    return { fixture, getAttachmentDownload };
  }

  it('keeps the action toolbar in the DOM but hidden without hover/focus styles active', async () => {
    const { fixture } = await setup(baseMessage());
    const toolbar = fixture.nativeElement.querySelector('[data-testid="msg-toolbar"]') as HTMLElement;
    expect(toolbar).toBeTruthy();

    const cmp = MessageBubble as unknown as { ɵcmp: { styles: string[] } };
    const css = cmp.ɵcmp.styles.join('\n');
    expect(css).toMatch(/\.vc-msg__toolbar/);
    expect(css).toMatch(/opacity:\s*0/);
    expect(css).toMatch(/\.vc-msg(?:\[[^\]]*\])?:hover/);
    expect(css).toMatch(/\.vc-msg(?:\[[^\]]*\])?:focus-within/);
    expect(css).toMatch(/\.vc-msg__toolbar--pinned/);
  });

  it('hides toolbar when message is not persisted', async () => {
    const { fixture } = await setup(baseMessage({ status: 'sending' }));
    expect(fixture.nativeElement.querySelector('[data-testid="msg-toolbar"]')).toBeNull();
  });

  it('filters more-menu affordance by mine / action flags', async () => {
    const theirs = await setup(baseMessage({ mine: false }), {
      showForwardAction: false,
      showThreadAction: false,
      showReplyAction: true,
    });
    expect(theirs.fixture.nativeElement.querySelector('[aria-label="Mais opções"]')).toBeNull();
    expect(theirs.fixture.nativeElement.querySelector('[aria-label="Responder"]')).toBeTruthy();

    const mine = await setup(baseMessage({ mine: true }), {
      showForwardAction: true,
      showThreadAction: true,
    });
    expect(mine.fixture.nativeElement.querySelector('[aria-label="Mais opções"]')).toBeTruthy();
    expect(mine.fixture.componentInstance.menuItems().map((i) => i.id)).toEqual([
      'forward',
      'thread',
      'edit',
      'delete',
    ]);
  });

  it('renders typed attachment preview instead of a plain download button for images', async () => {
    const { fixture } = await setup(
      baseMessage({
        body: '',
        attachments: [
          {
            id: 'a1',
            fileName: 'foto.png',
            contentType: 'image/png',
            sizeBytes: 1200,
            kind: 'File',
          },
        ],
      }),
    );
    expect(fixture.nativeElement.querySelector('vc-attachment-preview')).toBeTruthy();
  });

  it('pins the toolbar while the action menu is open', async () => {
    const { fixture } = await setup(baseMessage());
    const toolbar = fixture.nativeElement.querySelector('[data-testid="msg-toolbar"]') as HTMLElement;
    expect(toolbar.classList.contains('vc-msg__toolbar--pinned')).toBe(false);

    fixture.componentInstance.menuOpen.set(true);
    fixture.detectChanges();
    expect(toolbar.classList.contains('vc-msg__toolbar--pinned')).toBe(true);

    fixture.componentInstance.menuOpen.set(false);
    fixture.detectChanges();
    expect(toolbar.classList.contains('vc-msg__toolbar--pinned')).toBe(false);
  });

  it('always fetches a fresh download URL even when a cached preview URL exists', async () => {
    const getAttachmentDownload = vi
      .fn()
      .mockResolvedValue({ downloadUrl: 'https://example.test/fresh' });
    const openSpy = vi.spyOn(window, 'open').mockImplementation(() => null);

    const { fixture } = await setup(baseMessage(), {}, { getAttachmentDownload });
    fixture.componentInstance.downloadUrls.set({ a1: 'https://example.test/stale' });
    getAttachmentDownload.mockClear();

    await fixture.componentInstance.download({
      id: 'a1',
      fileName: 'doc.pdf',
      contentType: 'application/pdf',
      sizeBytes: 100,
      kind: 'File',
    });

    expect(getAttachmentDownload).toHaveBeenCalledWith('ch1', 'a1');
    expect(openSpy).toHaveBeenCalledWith(
      'https://example.test/fresh',
      '_blank',
      'noopener,noreferrer',
    );
    openSpy.mockRestore();
  });

  it('does not render a salva status tag for persisted messages', async () => {
    const { fixture } = await setup(baseMessage({ status: 'persisted' }));
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).not.toMatch(/\bsalva\b/);
  });

  it('hides header and avatar when grouping continuation inputs are off', async () => {
    const { fixture } = await setup(baseMessage({ mine: false, authorName: 'Alice Mendes' }), {
      showMeta: false,
      showAvatar: false,
      groupRole: 'end',
    });
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('.vc-msg__meta')).toBeNull();
    expect(root.querySelector('vc-avatar')).toBeNull();
    expect(root.querySelector('.vc-msg--grouped')).toBeTruthy();
    expect(root.querySelector('.vc-msg--group-end')).toBeTruthy();
    expect(root.querySelector('.vc-msg__hover-time')).toBeTruthy();
  });

  it('applies plain surface styles for stack-embedded messages', async () => {
    const { fixture } = await setup(baseMessage({ mine: false }), {
      groupRole: 'middle',
      showMeta: false,
      showAvatar: false,
      surface: 'plain',
    });
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('.vc-msg--plain')).toBeTruthy();
    expect(root.querySelector('.vc-msg__avatar-slot')).toBeNull();

    const cmp = MessageBubble as unknown as { ɵcmp: { styles: string[] } };
    const css = cmp.ɵcmp.styles.join('\n');
    expect(css).toMatch(/vc-msg--plain/);
    expect(css).toMatch(/background:\s*transparent/);
  });
});
