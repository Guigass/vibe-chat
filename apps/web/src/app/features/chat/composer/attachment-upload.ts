export const MAX_ATTACHMENTS_PER_MESSAGE = 10;
export const MAX_ATTACHMENT_SIZE_BYTES = 10 * 1024 * 1024;
export const UPLOAD_CONCURRENCY = 3;

export const ALLOWED_ATTACHMENT_TYPES = new Set([
  'image/png',
  'image/jpeg',
  'image/webp',
  'image/gif',
  'application/pdf',
  'text/plain',
]);

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
}

export interface AttachmentValidationError {
  fileName: string;
  reason: string;
}

export function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
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
  return 'application/octet-stream';
}

export function validateAttachmentFile(
  file: File,
  maxSizeBytes = MAX_ATTACHMENT_SIZE_BYTES,
): AttachmentValidationError | null {
  const contentType = resolveContentType(file);
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

export type AttachmentIconKind = 'image' | 'pdf' | 'text' | 'file';

export function attachmentIconKind(contentType: string): AttachmentIconKind {
  if (contentType.startsWith('image/')) return 'image';
  if (contentType === 'application/pdf') return 'pdf';
  if (contentType.startsWith('text/')) return 'text';
  return 'file';
}
