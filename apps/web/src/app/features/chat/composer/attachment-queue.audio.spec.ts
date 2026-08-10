import '@angular/compiler';
import { Injector } from '@angular/core';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiService } from '../../../core/api/api.service';
import { ChannelStore } from '../../../core/services/channel.store';
import { AttachmentQueueService } from './attachment-queue.service';
import { RecordedAudio } from './audio-recorder.service';

describe('AttachmentQueueService.uploadRecordedAudio', () => {
  const initiateAttachmentUpload = vi.fn();
  const uploadFileToPresignedUrl = vi.fn();
  const completeAttachmentUpload = vi.fn();

  beforeEach(() => {
    initiateAttachmentUpload.mockReset();
    uploadFileToPresignedUrl.mockReset();
    completeAttachmentUpload.mockReset();
  });

  function createQueue(): AttachmentQueueService {
    const injector = Injector.create({
      providers: [
        AttachmentQueueService,
        {
          provide: ApiService,
          useValue: {
            initiateAttachmentUpload,
            uploadFileToPresignedUrl,
            completeAttachmentUpload,
          },
        },
        {
          provide: ChannelStore,
          useValue: {
            activeChannel: () => ({ id: 'channel-1' }),
          },
        },
      ],
    });
    return injector.get(AttachmentQueueService);
  }

  it('initiates with kind Audio, base content type, duration and waveform', async () => {
    const attachmentId = 'att-audio-1';
    initiateAttachmentUpload.mockResolvedValue({
      attachmentId,
      uploadUrl: 'https://minio.example/upload',
      requiredHeaders: {},
    });
    uploadFileToPresignedUrl.mockResolvedValue(undefined);
    completeAttachmentUpload.mockResolvedValue({
      id: attachmentId,
      fileName: 'voice.webm',
      contentType: 'audio/webm',
      sizeBytes: 4,
      status: 'Ready',
    });

    const recorded: RecordedAudio = {
      blob: new Blob([new Uint8Array([1, 2, 3, 4])], { type: 'audio/webm;codecs=opus' }),
      mimeType: 'audio/webm;codecs=opus',
      fileName: 'audio-1.webm',
      durationMs: 2_500,
      waveform: [10, 20, 30],
    };

    const queue = createQueue();
    const result = await queue.uploadRecordedAudio('channel-1', recorded);

    expect(result.attachmentId).toBe(attachmentId);
    expect(result.error).toBeUndefined();
    expect(initiateAttachmentUpload).toHaveBeenCalledWith({
      channelId: 'channel-1',
      fileName: 'audio-1.webm',
      contentType: 'audio/webm',
      sizeBytes: 4,
      kind: 'Audio',
      durationMs: 2_500,
      waveform: [10, 20, 30],
    });
    expect(uploadFileToPresignedUrl).toHaveBeenCalledOnce();
    const uploadedFile = uploadFileToPresignedUrl.mock.calls[0]?.[1] as File;
    expect(uploadedFile.type).toBe('audio/webm');
    expect(completeAttachmentUpload).toHaveBeenCalledWith('channel-1', attachmentId);
    expect(queue.readyAttachmentIds()).toEqual([attachmentId]);
  });

  it('returns a visible error when initiate fails', async () => {
    initiateAttachmentUpload.mockRejectedValue(new Error('presign failed'));

    const recorded: RecordedAudio = {
      blob: new Blob([new Uint8Array([1])], { type: 'audio/webm' }),
      mimeType: 'audio/webm',
      fileName: 'audio-2.webm',
      durationMs: 500,
      waveform: [5],
    };

    const queue = createQueue();
    const result = await queue.uploadRecordedAudio('channel-1', recorded);

    expect(result.attachmentId).toBeUndefined();
    expect(result.error).toBe('presign failed');
    expect(queue.hasFailed()).toBe(true);
  });
});
