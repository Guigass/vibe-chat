import { WritableSignal } from '@angular/core';
import { ApiService } from '../../../core/api/api.service';
import { fillTemplate, ui } from '../../../core/i18n/strings';
import {
  classifyAttachmentPreview,
  isGifContentType,
} from '../../attachments/attachment-preview';
import {
  ChatMessage,
  MessageAttachment,
  MessageForwardedFrom,
  MessageLinkPreview,
} from '../../models/chat.models';
import type { LightboxImage } from '../image-lightbox/image-lightbox';

export function visibleLinkPreviewOf(message: ChatMessage): MessageLinkPreview | null {
  const preview = message.linkPreview;
  if (!preview || message.deletedAt) return null;
  const status = (preview.status ?? 'Ready').toLowerCase();
  if (status === 'failed' || status === 'blocked') return null;
  if (status === 'pending' || status === 'ready') return preview;
  return null;
}

export function formatLinkPreviewLabel(preview: MessageLinkPreview): string {
  const title = (preview.title ?? '').trim();
  const site = (preview.siteName ?? '').trim();
  if (title && site) return `${title} — ${site}`;
  if (title) return title;
  if (site) return site;
  return preview.url;
}

export function formatForwardOrigin(origin: MessageForwardedFrom): string {
  const raw = (origin.channelName ?? '').trim();
  const looksLikeDmSlug = /^dm:/i.test(raw);
  const isDirect = origin.isDirect === true || looksLikeDmSlug || raw === 'DM';
  if (isDirect) {
    if (!raw || raw === 'DM' || looksLikeDmSlug) return 'DM';
    return raw.startsWith('@') ? raw : `@${raw}`;
  }
  if (!raw) return '#';
  if (raw.startsWith('#') || raw.startsWith('@')) return raw;
  return `#${raw}`;
}

export function lightboxImagesOf(
  message: ChatMessage,
  downloadUrls: Record<string, string>,
  previewUrls: Record<string, string>,
): LightboxImage[] {
  return (message.attachments ?? [])
    .filter((attachment) => classifyAttachmentPreview(attachment.contentType, attachment.kind) === 'image')
    .map((attachment) => ({
      id: attachment.id,
      url: downloadUrls[attachment.id] ?? previewUrls[attachment.id],
      alt: attachment.fileName,
    }))
    .filter((image): image is LightboxImage => !!image.url);
}

export function formatReactionAriaLabel(emoji: string, tip: string): string {
  return tip ? fillTemplate(ui.bubbleReactionTip, { emoji, tip }) : fillTemplate(ui.bubbleReaction, { emoji });
}

export function syncAttachmentUrls(
  message: ChatMessage,
  api: ApiService,
  previewUrls: WritableSignal<Record<string, string>>,
  downloadUrls: WritableSignal<Record<string, string>>,
): void {
  const attachments = message.attachments ?? [];
  const channelId = message.channelId;
  for (const attachment of attachments) {
    const kind = classifyAttachmentPreview(attachment.contentType, attachment.kind);
    if (kind === 'image' || kind === 'pdf') {
      const status = attachment.thumbnailStatus;
      if (status === 'Ready' && !previewUrls()[attachment.id]) {
        void loadPreviewUrl(api, channelId, attachment.id, previewUrls, downloadUrls);
      } else if ((!status || status === 'Failed') && !downloadUrls()[attachment.id] && kind === 'image') {
        void loadAttachmentDownloadUrl(api, channelId, attachment.id, downloadUrls);
      }
      if (isGifContentType(attachment.contentType) && !downloadUrls()[attachment.id]) {
        void loadAttachmentDownloadUrl(api, channelId, attachment.id, downloadUrls);
      }
    } else if ((kind === 'audio' || kind === 'video') && !downloadUrls()[attachment.id]) {
      void loadAttachmentDownloadUrl(api, channelId, attachment.id, downloadUrls);
    }
  }
}

export function syncLinkPreviewImage(
  preview: MessageLinkPreview | null,
  message: ChatMessage,
  api: ApiService,
  linkPreviewImageUrl: WritableSignal<string | null>,
): void {
  const channelId = message.channelId;
  const messageId = message.id;
  if (!preview || !preview.hasImage || (preview.status ?? '').toLowerCase() === 'pending') {
    linkPreviewImageUrl.set(null);
    return;
  }
  if (!channelId || !messageId || linkPreviewImageUrl()) return;
  void loadLinkPreviewImage(api, channelId, messageId, linkPreviewImageUrl);
}

export async function downloadMessageAttachment(
  api: ApiService,
  channelId: string | undefined,
  attachment: MessageAttachment,
  downloadUrls: WritableSignal<Record<string, string>>,
): Promise<void> {
  if (!channelId) return;
  try {
    const result = await api.getAttachmentDownload(channelId, attachment.id);
    downloadUrls.update((current) => ({ ...current, [attachment.id]: result.downloadUrl }));
    window.open(result.downloadUrl, '_blank', 'noopener,noreferrer');
  } catch {
    // keep UI quiet; connection banner / toast stack not present in MVP shell
  }
}

export async function transcribeMessageAttachment(
  api: ApiService,
  workspaceId: string | undefined,
  message: ChatMessage,
  attachment: MessageAttachment,
  transcript: WritableSignal<string | null>,
): Promise<void> {
  const channelId = message.channelId;
  if (!workspaceId || !channelId) return;
  try {
    const result = await api.transcribeAttachment({
      workspaceId,
      channelId,
      messageId: message.id,
      attachmentId: attachment.id,
    });
    transcript.set(result.text);
  } catch {
    transcript.set(ui.bubbleTranscriptUnavailable);
  }
}

export async function loadReactionTooltipText(
  api: ApiService,
  channelId: string | undefined,
  messageId: string,
  emoji: string,
  tooltips: WritableSignal<Record<string, string>>,
  isDemo: boolean,
): Promise<void> {
  if (tooltips()[emoji]) return;
  if (!channelId || isDemo) return;
  try {
    const result = await api.getReactionUsers(channelId, messageId, emoji);
    const names = result.users.slice(0, 10).map((user) => user.displayName);
    const extra =
      result.total > names.length ? fillTemplate(ui.bubbleAndMore, { n: result.total - names.length }) : '';
    const text = names.length ? `${names.join(', ')}${extra}` : '';
    tooltips.update((current) => ({ ...current, [emoji]: text }));
  } catch {
    // tooltip stays empty
  }
}

export async function loadAttachmentDownloadUrl(
  api: ApiService,
  channelId: string | undefined,
  attachmentId: string,
  downloadUrls: WritableSignal<Record<string, string>>,
): Promise<void> {
  if (!channelId) return;
  try {
    const result = await api.getAttachmentDownload(channelId, attachmentId);
    downloadUrls.update((current) => ({ ...current, [attachmentId]: result.downloadUrl }));
  } catch {
    // preview stays on file card until URL resolves
  }
}

async function loadPreviewUrl(
  api: ApiService,
  channelId: string | undefined,
  attachmentId: string,
  previewUrls: WritableSignal<Record<string, string>>,
  downloadUrls: WritableSignal<Record<string, string>>,
): Promise<void> {
  if (!channelId) return;
  try {
    const result = await api.getAttachmentThumbnail(channelId, attachmentId);
    previewUrls.update((current) => ({ ...current, [attachmentId]: result.downloadUrl }));
  } catch {
    if (!downloadUrls()[attachmentId]) {
      void loadAttachmentDownloadUrl(api, channelId, attachmentId, downloadUrls);
    }
  }
}

async function loadLinkPreviewImage(
  api: ApiService,
  channelId: string,
  messageId: string,
  linkPreviewImageUrl: WritableSignal<string | null>,
): Promise<void> {
  try {
    const result = await api.getLinkPreviewImage(channelId, messageId);
    linkPreviewImageUrl.set(result.downloadUrl);
  } catch {
    // card stays text-only when image URL is unavailable
  }
}
