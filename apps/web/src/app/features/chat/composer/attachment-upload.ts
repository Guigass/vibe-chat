export const MAX_ATTACHMENTS_PER_MESSAGE = 10;
export const MAX_ATTACHMENT_SIZE_BYTES = 10 * 1024 * 1024;
export const MAX_VIDEO_SIZE_BYTES = 25 * 1024 * 1024;
export const MAX_VIDEO_DURATION_MS = 60_000;
export const UPLOAD_CONCURRENCY = 3;

export const ALLOWED_ATTACHMENT_TYPES = new Set([
  'image/png',
  'image/jpeg',
  'image/webp',
  'image/gif',
  'application/pdf',
  'text/plain',
]);

export const ALLOWED_VIDEO_TYPES = new Set(['video/mp4', 'video/webm']);

export type PendingAttachmentStatus =
  | 'validating'
  | 'queued'
  | 'uploading'
  | 'ready'
  | 'failed';

export interface PendingAttachment {
  localId: string;
  file: File;
  status: PendingAttachmentStatus;
  progress: number;
  error?: string;
  attachmentId?: string;
  /** Display size when restoring a ready attachment by id (no bytes on disk). */
  restoredSizeBytes?: number;
  uploadKind?: 'File' | 'Video';
  durationMs?: number;
  width?: number;
  height?: number;
  /** Object URL for local video preview; revoke on remove/clear. */
  previewUrl?: string;
}

export interface AttachmentValidationError {
  fileName: string;
  reason: string;
}

export interface VideoMetadata {
  durationMs: number;
  width?: number;
  height?: number;
}

export function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

export function formatVideoDuration(ms: number): string {
  const totalSec = Math.max(0, Math.floor(ms / 1000));
  const min = Math.floor(totalSec / 60);
  const sec = totalSec % 60;
  return `${min}:${sec.toString().padStart(2, '0')}`;
}

export function resolveContentType(file: File): string {
  const type = file.type?.trim();
  if (type) return type;
  const lower = file.name.toLowerCase();
  if (lower.endsWith('.png')) return 'image/png';
  if (lower.endsWith('.jpg') || lower.endsWith('.jpeg')) return 'image/jpeg';
  if (lower.endsWith('.webp')) return 'image/webp';
  if (lower.endsWith('.gif')) return 'image/gif';
  if (lower.endsWith('.pdf')) return 'application/pdf';
  if (lower.endsWith('.txt')) return 'text/plain';
  if (lower.endsWith('.mp4')) return 'video/mp4';
  if (lower.endsWith('.webm')) return 'video/webm';
  return 'application/octet-stream';
}

export function isVideoContentType(contentType: string): boolean {
  const type = contentType.trim().toLowerCase();
  return type === 'video/mp4' || type.startsWith('video/webm');
}

export function validateAttachmentFile(
  file: File,
  maxSizeBytes = MAX_ATTACHMENT_SIZE_BYTES,
): AttachmentValidationError | null {
  const contentType = resolveContentType(file);
  if (isVideoContentType(contentType)) {
    return validateVideoAttachmentFile(file, contentType);
  }
  if (!ALLOWED_ATTACHMENT_TYPES.has(contentType)) {
    return {
      fileName: file.name,
      reason: `tipo não permitido (${contentType || 'desconhecido'})`,
    };
  }
  if (file.size <= 0) {
    return { fileName: file.name, reason: 'arquivo vazio' };
  }
  if (file.size > maxSizeBytes) {
    return {
      fileName: file.name,
      reason: `excede ${formatFileSize(maxSizeBytes)} (tem ${formatFileSize(file.size)})`,
    };
  }
  return null;
}

export function validateVideoAttachmentFile(
  file: File,
  contentType = resolveContentType(file),
  durationMs?: number,
): AttachmentValidationError | null {
  const normalized = contentType.trim().toLowerCase();
  if (!ALLOWED_VIDEO_TYPES.has(normalized) && !normalized.startsWith('video/webm')) {
    return {
      fileName: file.name,
      reason: `tipo de vídeo não permitido (${contentType || 'desconhecido'})`,
    };
  }
  if (file.size <= 0) {
    return { fileName: file.name, reason: 'arquivo vazio' };
  }
  if (file.size > MAX_VIDEO_SIZE_BYTES) {
    return {
      fileName: file.name,
      reason: `excede ${formatFileSize(MAX_VIDEO_SIZE_BYTES)} (tem ${formatFileSize(file.size)})`,
    };
  }
  if (durationMs != null && durationMs > MAX_VIDEO_DURATION_MS) {
    return {
      fileName: file.name,
      reason: `excede ${formatVideoDuration(MAX_VIDEO_DURATION_MS)} de duração`,
    };
  }
  return null;
}

export function extractVideoMetadata(file: File): Promise<VideoMetadata> {
  return new Promise((resolve, reject) => {
    const url = URL.createObjectURL(file);
    const video = document.createElement('video');
    video.preload = 'metadata';
    video.onloadedmetadata = () => {
      const durationMs = Number.isFinite(video.duration)
        ? Math.round(video.duration * 1000)
        : 0;
      const width = video.videoWidth > 0 ? video.videoWidth : undefined;
      const height = video.videoHeight > 0 ? video.videoHeight : undefined;
      URL.revokeObjectURL(url);
      if (durationMs <= 0) {
        reject(new Error('Não foi possível ler a duração do vídeo'));
        return;
      }
      resolve({ durationMs, width, height });
    };
    video.onerror = () => {
      URL.revokeObjectURL(url);
      reject(new Error('Não foi possível ler metadados do vídeo'));
    };
    video.src = url;
  });
}

export function collectFilesFromDataTransfer(dataTransfer: DataTransfer | null): File[] {
  if (!dataTransfer?.types.includes('Files')) {
    return [];
  }
  return Array.from(dataTransfer.files ?? []);
}

export function collectFilesFromClipboard(dataTransfer: DataTransfer | null): File[] {
  if (!dataTransfer) return [];
  const files = Array.from(dataTransfer.files ?? []);
  if (files.length > 0) return files;
  for (const item of Array.from(dataTransfer.items ?? [])) {
    if (item.kind === 'file') {
      const file = item.getAsFile();
      if (file) files.push(file);
    }
  }
  return files;
}

export type AttachmentIconKind = 'image' | 'pdf' | 'text' | 'video' | 'file';

export function attachmentIconKind(contentType: string): AttachmentIconKind {
  if (contentType.startsWith('image/')) return 'image';
  if (contentType === 'application/pdf') return 'pdf';
  if (contentType.startsWith('text/')) return 'text';
  if (isVideoContentType(contentType)) return 'video';
  return 'file';
}
