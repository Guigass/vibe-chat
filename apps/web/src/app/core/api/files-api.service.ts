import { Injectable } from '@angular/core';
import { HttpApiClient } from './http-api.client';
import { mapAttachment, type AttachmentDto } from './api-mappers';
import { MessageAttachment } from '../../shared/models/chat.models';

interface AttachmentUploadDto {
  attachmentId: string;
  uploadUrl: string;
  expiresAt: string;
  requiredHeaders: Record<string, string>;
  maxSizeBytes: number;
  fileName: string;
  contentType: string;
}

interface AttachmentDownloadDto {
  attachmentId: string;
  downloadUrl: string;
  expiresAt: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
}

interface LinkPreviewImageDto {
  messageId?: string;
  downloadUrl: string;
  expiresAt: string;
  contentType: string;
}

@Injectable({ providedIn: 'root' })
export class FilesApiService extends HttpApiClient {
  async initiateAttachmentUpload(input: {
    channelId: string;
    fileName: string;
    contentType: string;
    sizeBytes: number;
    kind?: 'File' | 'Audio' | 'Video';
    durationMs?: number;
    waveform?: number[];
    width?: number;
    height?: number;
  }): Promise<AttachmentUploadDto> {
    return this.request<AttachmentUploadDto>(`/api/v1/channels/${input.channelId}/attachments`, {
      method: 'POST',
      body: JSON.stringify({
        fileName: input.fileName,
        contentType: input.contentType,
        sizeBytes: input.sizeBytes,
        kind: input.kind,
        durationMs: input.durationMs,
        waveform: input.waveform,
        width: input.width,
        height: input.height,
      }),
    });
  }

  async completeAttachmentUpload(channelId: string, attachmentId: string): Promise<MessageAttachment> {
    const dto = await this.request<AttachmentDto>(
      `/api/v1/channels/${channelId}/attachments/${attachmentId}/complete`,
      { method: 'POST', body: '{}' },
    );
    return mapAttachment(dto);
  }

  async transcribeAttachment(input: {
    workspaceId: string;
    channelId: string;
    messageId: string;
    attachmentId: string;
  }): Promise<{ text: string; language: string; provider: string }> {
    return this.request(`/api/v1/workspaces/${input.workspaceId}/channels/${input.channelId}/messages/${input.messageId}/attachments/${input.attachmentId}/transcribe`, {
      method: 'POST',
      body: '{}',
    });
  }

  async getAttachmentDownload(
    channelId: string,
    attachmentId: string,
  ): Promise<AttachmentDownloadDto> {
    return this.request<AttachmentDownloadDto>(
      `/api/v1/channels/${channelId}/attachments/${attachmentId}/download`,
    );
  }

  async getAttachmentThumbnail(
    channelId: string,
    attachmentId: string,
  ): Promise<AttachmentDownloadDto> {
    return this.request<AttachmentDownloadDto>(
      `/api/v1/channels/${channelId}/attachments/${attachmentId}/thumbnail`,
    );
  }

  async getLinkPreviewImage(
    channelId: string,
    messageId: string,
  ): Promise<LinkPreviewImageDto> {
    return this.request<LinkPreviewImageDto>(
      `/api/v1/channels/${channelId}/messages/${messageId}/link-preview/image`,
    );
  }

  async uploadFileToPresignedUrl(
    uploadUrl: string,
    file: File,
    requiredHeaders: Record<string, string>,
    onProgress?: (percent: number) => void,
    signal?: AbortSignal,
  ): Promise<void> {
    await new Promise<void>((resolve, reject) => {
      const xhr = new XMLHttpRequest();
      xhr.open('PUT', uploadUrl);
      for (const [key, value] of Object.entries(requiredHeaders ?? {})) {
        xhr.setRequestHeader(key, value);
      }
      xhr.upload.onprogress = (event) => {
        if (!event.lengthComputable) return;
        onProgress?.(Math.round((event.loaded / event.total) * 100));
      };
      xhr.onload = () => {
        if (xhr.status >= 200 && xhr.status < 300) {
          onProgress?.(100);
          resolve();
          return;
        }
        reject(new Error(`Upload failed (${xhr.status})`));
      };
      xhr.onerror = () => reject(new Error('Upload failed (network)'));
      xhr.onabort = () => reject(new DOMException('Upload aborted', 'AbortError'));
      signal?.addEventListener('abort', () => xhr.abort(), { once: true });
      xhr.send(file);
    });
  }
}
