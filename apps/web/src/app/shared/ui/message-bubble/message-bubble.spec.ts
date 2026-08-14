import { TestBed } from '@angular/core/testing';
import { describe, expect, it, vi } from 'vitest';
import { MessageBubble } from './message-bubble';
import { ApiService } from '../../../core/api/api.service';
import { AuthService } from '../../../core/auth/auth.service';
import { ChannelStore } from '../../../core/services/channel.store';
import { MessageStore } from '../../../core/services/message.store';
import { ThemeService } from '../../../core/services/theme.service';
import type { ChatMessage } from '../../models/chat.models';
import { userMentionToken } from '../../markdown/mention-tokens';

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
      getAttachmentThumbnail: ReturnType<typeof vi.fn>;
      getLinkPreviewImage: ReturnType<typeof vi.fn>;
    }> = {},
    channelOverrides: { mentionLabels?: Record<string, string> } = {},
  ) {
    TestBed.resetTestingModule();
    const getAttachmentDownload =
      apiOverrides.getAttachmentDownload ??
      vi.fn().mockResolvedValue({ downloadUrl: 'https://example.test/f' });
    const getAttachmentThumbnail =
      apiOverrides.getAttachmentThumbnail ??
      vi.fn().mockResolvedValue({ downloadUrl: 'https://example.test/thumb.webp' });
    const getLinkPreviewImage =
      apiOverrides.getLinkPreviewImage ??
      vi.fn().mockResolvedValue({
        downloadUrl: 'https://example.test/og.webp',
        expiresAt: new Date().toISOString(),
        contentType: 'image/webp',
      });
    const openDirectMessage = vi.fn().mockResolvedValue({ id: 'dm-1' });
    const loadChannel = vi.fn().mockResolvedValue(undefined);
    await TestBed.configureTestingModule({
      imports: [MessageBubble],
      providers: [
        {
          provide: ApiService,
          useValue: {
            getAttachmentDownload,
            getAttachmentThumbnail,
            getLinkPreviewImage,
            getReactionUsers: vi.fn(),
            transcribeAttachment: vi.fn(),
          },
        },
        {
          provide: ChannelStore,
          useValue: {
            mentionLabels: () => channelOverrides.mentionLabels ?? {},
            activeWorkspace: () => ({ id: 'w1' }),
            isDemo: () => true,
            openDirectMessage,
          },
        },
        {
          provide: MessageStore,
          useValue: { loadChannel },
        },
        {
          provide: AuthService,
          useValue: { profile: () => ({ id: 'u-alice', name: 'Alice' }) },
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
    return { fixture, getAttachmentDownload, openDirectMessage, loadChannel };
  }

  it('keeps the action toolbar in the DOM but hidden without hover/focus styles active', async () => {
    const { fixture } = await setup(baseMessage());
    const toolbar = fixture.nativeElement.querySelector(
      '[data-testid="msg-toolbar"]',
    ) as HTMLElement;
    expect(toolbar).toBeTruthy();

    const cmp = MessageBubble as unknown as { ɵcmp: { styles: string[] } };
    const css = cmp.ɵcmp.styles.join('\n');
    expect(css).toMatch(/\.vc-msg__toolbar/);
    expect(css).toMatch(/opacity:\s*0/);
    expect(css).toMatch(/\.vc-msg__toolbar(?:\[[^\]]+\])?\s*{[^}]*position:\s*absolute/s);
    expect(css).toMatch(/\.vc-msg__toolbar(?:\[[^\]]+\])?\s*{[^}]*right:\s*0/s);
    expect(css).toMatch(
      /\.vc-msg--mine(?:\[[^\]]+\])?\s+\.vc-msg__toolbar(?:\[[^\]]+\])?\s*{[^}]*left:\s*0/s,
    );
    expect(css).toMatch(/--(?:%NS%)?vc-msg-toolbar-shift:\s*-100%/);
    expect(css).toMatch(/\.vc-msg__more-dots/);
    expect(css).not.toMatch(/\.vc-msg__body(?:\[[^\]]+\])?\s*{[^}]*padding-right:\s*calc/s);
    expect(css).not.toMatch(/\.vc-msg__toolbar(?:\[[^\]]+\])?\s*{[^}]*max-height/s);
    expect(css).toMatch(/\.vc-msg-menu__reactions/);
    expect(css).toMatch(/\.vc-msg(?:\[[^\]]*\])?:hover/);
    expect(css).toMatch(/\.vc-msg(?:\[[^\]]*\])?:focus-within/);
    expect(css).toMatch(/\.vc-msg__toolbar--pinned/);
  });

  it('hides toolbar when message is not persisted', async () => {
    const { fixture } = await setup(baseMessage({ status: 'sending' }));
    expect(fixture.nativeElement.querySelector('[data-testid="msg-toolbar"]')).toBeNull();
  });

  it('keeps reaction aria-label prefixed with Reação even after tooltip loads', async () => {
    const { fixture } = await setup(
      baseMessage({
        reactions: [{ emoji: '👍', count: 1, me: false }],
      }),
    );
    const cmp = fixture.componentInstance;
    expect(cmp.reactionAriaLabel('👍')).toBe('Reação 👍');
    cmp.reactionTooltips.set({ '👍': 'Bob' });
    fixture.detectChanges();
    expect(cmp.reactionAriaLabel('👍')).toBe('Reação 👍: Bob');
    const button = fixture.nativeElement.querySelector(
      '.vc-msg__reactions button',
    ) as HTMLButtonElement;
    expect(button.getAttribute('aria-label')).toMatch(/^Reação 👍/);
  });

  it('keeps one stable menu trigger and filters actions by mine / flags', async () => {
    const theirs = await setup(baseMessage({ mine: false }), {
      showForwardAction: false,
      showThreadAction: false,
      showReplyAction: true,
    });
    expect(
      theirs.fixture.nativeElement.querySelector('[aria-label="Ações da mensagem"]'),
    ).toBeTruthy();
    expect(theirs.fixture.nativeElement.querySelector('[aria-label="Responder"]')).toBeNull();
    expect(theirs.fixture.componentInstance.menuItems()).toEqual([]);
    expect(theirs.fixture.componentInstance.actionMenuPositions()[0]).toMatchObject({
      originX: 'end',
      overlayX: 'start',
    });

    const mine = await setup(baseMessage({ mine: true }), {
      showForwardAction: true,
      showThreadAction: true,
    });
    expect(
      mine.fixture.nativeElement.querySelector('[aria-label="Ações da mensagem"]'),
    ).toBeTruthy();
    expect(mine.fixture.componentInstance.menuItems().map((i) => i.id)).toEqual([
      'forward',
      'thread',
      'edit',
      'delete',
    ]);
    expect(mine.fixture.componentInstance.actionMenuPositions()[0]).toMatchObject({
      originX: 'start',
      overlayX: 'end',
    });
  });

  it('emits startEdit from menu without inline textarea (B-173)', async () => {
    const { fixture } = await setup(baseMessage({ mine: true, status: 'persisted' }));
    const startEditSpy = vi.fn();
    fixture.componentInstance.startEdit.subscribe(startEditSpy);

    fixture.componentInstance.onMenuAction('edit');
    fixture.detectChanges();

    expect(startEditSpy).toHaveBeenCalledTimes(1);
    expect(fixture.nativeElement.querySelector('.vc-msg__edit')).toBeNull();
    expect(fixture.nativeElement.querySelector('textarea')).toBeNull();
    expect(fixture.nativeElement.querySelector('vc-markdown-body')).toBeTruthy();
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
    const toolbar = fixture.nativeElement.querySelector(
      '[data-testid="msg-toolbar"]',
    ) as HTMLElement;
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

  it('loads thumbnail URL for Ready image attachments (B-090)', async () => {
    const getAttachmentThumbnail = vi
      .fn()
      .mockResolvedValue({ downloadUrl: 'https://example.test/thumb.webp' });
    const getAttachmentDownload = vi.fn();
    const { fixture } = await setup(
      baseMessage({
        body: '',
        attachments: [
          {
            id: 'a1',
            fileName: 'shot.png',
            contentType: 'image/png',
            sizeBytes: 1200,
            kind: 'File',
            thumbnailStatus: 'Ready',
            width: 800,
            height: 600,
          },
        ],
      }),
      {},
      { getAttachmentThumbnail, getAttachmentDownload },
    );

    await vi.waitFor(() => {
      expect(getAttachmentThumbnail).toHaveBeenCalledWith('ch1', 'a1');
    });
    expect(getAttachmentDownload).not.toHaveBeenCalled();
    expect(fixture.componentInstance.previewUrls()['a1']).toBe('https://example.test/thumb.webp');
  });

  it('renders link preview card and loads image when Ready (B-091)', async () => {
    const getLinkPreviewImage = vi.fn().mockResolvedValue({
      downloadUrl: 'https://example.test/og.webp',
      expiresAt: new Date().toISOString(),
      contentType: 'image/webp',
    });
    const { fixture } = await setup(
      baseMessage({
        body: 'veja https://example.com',
        linkPreview: {
          id: 'lp1',
          url: 'https://example.com',
          title: 'Example Site',
          description: 'A demo page',
          siteName: 'example.com',
          hasImage: true,
          status: 'Ready',
        },
      }),
      {},
      { getLinkPreviewImage },
    );

    const card = fixture.nativeElement.querySelector('a.vc-msg__link-preview') as HTMLAnchorElement;
    expect(card).toBeTruthy();
    expect(card.href).toContain('https://example.com');
    expect(card.target).toBe('_blank');
    expect(card.rel).toContain('noopener');
    expect(fixture.componentInstance.menuItems().map((i) => i.id)).toContain('remove-link-preview');

    await vi.waitFor(() => {
      expect(getLinkPreviewImage).toHaveBeenCalledWith('ch1', 'm1');
    });
    expect(fixture.componentInstance.linkPreviewImageUrl()).toBe('https://example.test/og.webp');
  });

  it('does not render a salva status tag for persisted messages', async () => {
    const { fixture } = await setup(baseMessage({ status: 'persisted' }));
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('[aria-label="Mensagem salva"]')).toBeNull();
    expect(root.textContent ?? '').not.toMatch(/\bsalva\b/);
  });

  it('renders icon status markers with title and aria-label', async () => {
    const { fixture } = await setup(
      baseMessage({
        editedAt: new Date().toISOString(),
        isPinned: true,
        isSaved: true,
        status: 'persisted',
      }),
    );
    const root = fixture.nativeElement as HTMLElement;

    const edited = root.querySelector('[aria-label="Editada"]') as HTMLElement;
    const pinned = root.querySelector('[aria-label="Mensagem fixada"]') as HTMLElement;
    const saved = root.querySelector('[aria-label="Mensagem salva"]') as HTMLElement;

    expect(edited).toBeTruthy();
    expect(edited.getAttribute('title')).toBe('Editada');
    expect(edited.querySelector('svg')).toBeTruthy();

    expect(pinned).toBeTruthy();
    expect(pinned.getAttribute('title')).toBe('Mensagem fixada');
    expect(pinned.querySelector('svg')).toBeTruthy();

    expect(saved).toBeTruthy();
    expect(saved.getAttribute('title')).toBe('Mensagem salva');
    expect(saved.querySelector('svg')).toBeTruthy();

    expect(root.textContent ?? '').not.toMatch(/\beditada\b/);
    expect(root.textContent ?? '').not.toMatch(/\bfixada\b/);
    expect(root.textContent ?? '').not.toMatch(/\bsalva\b/);
  });

  it('renders edited and saved icons in group hover meta when meta is hidden', async () => {
    const { fixture } = await setup(
      baseMessage({
        editedAt: new Date().toISOString(),
        isSaved: true,
        isPinned: true,
        mine: true,
      }),
      { showMeta: false, groupRole: 'end' },
    );
    const root = fixture.nativeElement as HTMLElement;
    const cluster = root.querySelector('.vc-msg__group-meta') as HTMLElement;
    expect(cluster).toBeTruthy();
    expect(cluster.querySelector('[aria-label="Editada"]')).toBeTruthy();
    expect(cluster.querySelector('[aria-label="Mensagem salva"]')).toBeTruthy();
    expect(cluster.querySelector('[aria-label="Mensagem fixada"]')).toBeTruthy();
    expect(cluster.querySelector('time')).toBeTruthy();
    expect(root.querySelector('.vc-msg__edited-badge')).toBeNull();
  });

  it('places grouped hover meta toward the screen center', async () => {
    const cmp = MessageBubble as unknown as { ɵcmp: { styles: string[] } };
    const css = cmp.ɵcmp.styles.join('\n');
    // Theirs (left): meta on the right of the bubble (toward center)
    expect(css).toMatch(
      /\.vc-msg__group-meta(?:\[[^\]]+\])?\s*{[^}]*left:\s*calc\(\s*100%\s*\+\s*1\.9rem\s*\)/s,
    );
    // Mine (right): meta on the left of the bubble (toward center)
    expect(css).toMatch(
      /\.vc-msg--mine(?:\[[^\]]+\])?\s+\.vc-msg__group-meta(?:\[[^\]]+\])?\s*{[^}]*right:\s*calc\(\s*100%\s*\+\s*1\.9rem\s*\)/s,
    );
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
    expect(root.querySelector('.vc-msg__group-meta')).toBeTruthy();
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
    expect(root.querySelector('.vc-msg__body')).toBeTruthy();
  });

  it('opens a DM when a mention chip is clicked', async () => {
    const bob = '55555555-5555-5555-5555-555555555555';
    const { fixture, openDirectMessage, loadChannel } = await setup(
      baseMessage({
        mine: false,
        body: `oi ${userMentionToken(bob)}`,
      }),
      {},
      {},
      { mentionLabels: { [bob]: 'Bob' } },
    );

    const chip = fixture.nativeElement.querySelector('button.vc-md__mention') as HTMLButtonElement;
    expect(chip).toBeTruthy();
    expect(chip.textContent).toContain('@Bob');
    chip.click();
    await fixture.whenStable();

    expect(openDirectMessage).toHaveBeenCalledWith(bob);
    expect(loadChannel).toHaveBeenCalledWith('dm-1');
  });
});
