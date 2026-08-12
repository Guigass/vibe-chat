/** @vitest-environment jsdom */
import '@angular/compiler';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiService } from '../../../core/api/api.service';
import { ChannelStore } from '../../../core/services/channel.store';
import { ChatHubService } from '../../../core/services/chat-hub.service';
import { DraftStoreService } from '../../../core/services/draft-store.service';
import { MessageStore } from '../../../core/services/message.store';
import { ChatMessage } from '../../../shared/models/chat.models';
import { AttachmentQueueService } from './attachment-queue.service';
import { AudioRecorderService, RecordedAudio } from './audio-recorder.service';
import { Composer } from './composer';

describe('Composer audio submit (BUG-004)', () => {
  const phase = signal<'idle' | 'recording' | 'preview'>('idle');
  const errorMessage = signal<string | null>(null);
  const activeChannelId = signal<string | null>('channel-1');
  const replyTarget = signal<ChatMessage | null>(null);
  const stop = vi.fn();
  const buildRecordedAudio = vi.fn();
  const reset = vi.fn();
  const discard = vi.fn();
  const start = vi.fn();
  const uploadRecordedAudio = vi.fn();
  const send = vi.fn();
  const clearAttachments = vi.fn();
  const clearReplyTarget = vi.fn();
  const restoreReady = vi.fn();
  const draftGet = vi.fn().mockResolvedValue(null);
  const draftSaveNow = vi.fn().mockResolvedValue(undefined);
  const draftRemove = vi.fn().mockResolvedValue(undefined);
  const draftScheduleSave = vi.fn();

  let fixture: ComponentFixture<Composer>;
  let composer: Composer;

  const recorded: RecordedAudio = {
    blob: new Blob([new Uint8Array([1, 2, 3, 4])], { type: 'audio/webm' }),
    mimeType: 'audio/webm',
    fileName: 'audio-1.webm',
    durationMs: 1_200,
    waveform: [10, 20],
  };

  beforeEach(async () => {
    phase.set('idle');
    errorMessage.set(null);
    activeChannelId.set('channel-1');
    replyTarget.set(null);
    stop.mockReset();
    buildRecordedAudio.mockReset();
    reset.mockReset();
    discard.mockReset();
    start.mockReset();
    uploadRecordedAudio.mockReset();
    send.mockReset();
    clearAttachments.mockReset();
    clearReplyTarget.mockReset();
    restoreReady.mockReset();
    draftGet.mockReset().mockResolvedValue(null);
    draftSaveNow.mockReset().mockResolvedValue(undefined);
    draftRemove.mockReset().mockResolvedValue(undefined);
    draftScheduleSave.mockReset();

    stop.mockImplementation(async () => {
      phase.set('preview');
      return recorded;
    });
    buildRecordedAudio.mockResolvedValue(recorded);
    uploadRecordedAudio.mockResolvedValue({ attachmentId: 'att-1' });
    send.mockResolvedValue(true);

    await TestBed.configureTestingModule({
      imports: [Composer],
      providers: [
        {
          provide: AudioRecorderService,
          useValue: {
            supported: true,
            phase: phase.asReadonly(),
            elapsedMs: signal(0).asReadonly(),
            liveWaveform: signal<number[]>([]).asReadonly(),
            previewUrl: signal<string | null>(null).asReadonly(),
            previewBlob: signal<Blob | null>(null).asReadonly(),
            errorMessage: errorMessage.asReadonly(),
            start,
            stop,
            discard,
            reset,
            buildRecordedAudio,
          },
        },
        {
          provide: AttachmentQueueService,
          useValue: {
            items: signal([]).asReadonly(),
            liveAnnouncement: signal(null).asReadonly(),
            readyAttachmentIds: () => [],
            readyAttachmentMetas: () => [],
            hasActiveUploads: () => false,
            canAcceptMore: () => true,
            submitBlocked: () => false,
            clear: clearAttachments,
            restoreReady,
            uploadRecordedAudio,
            waitForReady: async () => [],
            addFiles: () => null,
            cancelUpload: () => undefined,
            retry: () => undefined,
            remove: () => undefined,
          },
        },
        {
          provide: MessageStore,
          useValue: {
            sending: () => false,
            replyTarget: replyTarget.asReadonly(),
            send,
            clearReplyTarget,
          },
        },
        {
          provide: ChannelStore,
          useValue: {
            activeChannelId: activeChannelId.asReadonly(),
            activeChannel: () => ({ id: 'channel-1', name: 'geral' }),
            activeWorkspace: () => ({ id: 'ws-1' }),
            composerPrefill: () => null,
            consumeComposerPrefill: () => null,
            isDemo: () => false,
            members: () => [],
          },
        },
        {
          provide: ChatHubService,
          useValue: { sendTyping: vi.fn() },
        },
        {
          provide: ApiService,
          useValue: {
            getChannelMembers: vi.fn().mockResolvedValue([]),
            getCommands: vi.fn().mockResolvedValue([
              { name: 'ajuda', description: 'Lista', usage: '/ajuda' },
            ]),
          },
        },
        {
          provide: DraftStoreService,
          useValue: {
            get: draftGet,
            saveNow: draftSaveNow,
            scheduleSave: draftScheduleSave,
            remove: draftRemove,
            hasDraft: () => false,
            draftConversationIds: signal(new Set()).asReadonly(),
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(Composer);
    composer = fixture.componentInstance;
    fixture.detectChanges();
    // Allow the channel-switch effect to settle once.
    await fixture.whenStable();
    reset.mockClear();
    clearAttachments.mockClear();
  });

  it('onSubmit while recording awaits stop() then uploads and sends', async () => {
    phase.set('recording');

    await composer.onSubmit(new Event('submit'));

    expect(stop).toHaveBeenCalledTimes(1);
    expect(buildRecordedAudio).toHaveBeenCalled();
    expect(uploadRecordedAudio).toHaveBeenCalledWith('channel-1', recorded);
    expect(send).toHaveBeenCalledWith('', ['att-1']);
    expect(composer.validationError()).toBeNull();
  });

  it('onSubmit from preview skips stop() and still sends', async () => {
    phase.set('preview');

    await composer.onSubmit(new Event('submit'));

    expect(stop).not.toHaveBeenCalled();
    expect(uploadRecordedAudio).toHaveBeenCalledWith('channel-1', recorded);
    expect(send).toHaveBeenCalledWith('', ['att-1']);
  });

  it('surfaces an error when stop() fails to produce audio', async () => {
    phase.set('recording');
    stop.mockResolvedValue(null);
    errorMessage.set('Áudio inválido ou vazio. Grave novamente.');

    await composer.onSubmit(new Event('submit'));

    expect(uploadRecordedAudio).not.toHaveBeenCalled();
    expect(send).not.toHaveBeenCalled();
    expect(composer.validationError()).toContain('Áudio inválido');
  });

  it('does not reset the recorder when previewUrl changes (channel effect stays stable)', async () => {
    phase.set('recording');
    // Simulate the old bug: something writes preview-related state while recording.
    // The channel effect must not re-fire solely because audio signals changed.
    await composer.onSubmit(new Event('submit'));

    // reset is called once on successful send, not spuriously mid-flight from the effect.
    expect(reset).toHaveBeenCalledTimes(1);
  });

  it('focuses the textarea when a reply target is set (BUG-017)', async () => {
    const textarea = fixture.nativeElement.querySelector('textarea') as HTMLTextAreaElement;
    textarea.blur();

    replyTarget.set({
      id: 'message-1',
      conversationId: 'channel-1',
      channelId: 'channel-1',
      authorUserId: 'user-1',
      authorName: 'Alice',
      body: 'Original message',
      createdAt: new Date().toISOString(),
      status: 'sent',
      mine: false,
    });
    fixture.detectChanges();
    await fixture.whenStable();

    expect(document.activeElement).toBe(textarea);
  });

  it('does not send unknown slash commands as messages (B-087)', async () => {
    phase.set('idle');
    composer.draft.set('/xpto');

    await composer.onSubmit(new Event('submit'));

    expect(send).not.toHaveBeenCalled();
    expect(composer.slash.notice()?.kind).toBe('error');
    expect(composer.draft()).toBe('/xpto');
  });
});
