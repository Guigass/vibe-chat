import { ChatMessage, MessageAttachment } from '../../shared/models/chat.models';

export interface AttachmentDto {
  id: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  status?: string;
  kind?: string;
  durationMs?: number;
  waveform?: number[];
  thumbnailStatus?: string | null;
  width?: number | null;
  height?: number | null;
  pageCount?: number | null;
}

export function mapAttachment(a: AttachmentDto): MessageAttachment {
  return {
    id: a.id,
    fileName: a.fileName,
    contentType: a.contentType,
    sizeBytes: a.sizeBytes,
    status: a.status,
    kind: a.kind === 'Audio' ? 'Audio' : a.kind === 'Video' ? 'Video' : 'File',
    durationMs: a.durationMs,
    waveform: a.waveform,
    thumbnailStatus: a.thumbnailStatus ?? null,
    width: a.width ?? null,
    height: a.height ?? null,
    pageCount: a.pageCount ?? null,
  };
}

export function mapMoveFields(raw: object): Pick<
  ChatMessage,
  | 'movedToMessageId'
  | 'movedToChannelId'
  | 'movedToSequence'
  | 'movedToAccessible'
  | 'movedFromMessageId'
  | 'movedFromChannelId'
  | 'movedFromSequence'
> {
  const row = raw as {
    movedToMessageId?: string | null;
    movedToChannelId?: string | null;
    movedToSequence?: number | null;
    movedToAccessible?: boolean;
    movedFromMessageId?: string | null;
    movedFromChannelId?: string | null;
    movedFromSequence?: number | null;
  };
  return {
    movedToMessageId: row.movedToMessageId ?? null,
    movedToChannelId: row.movedToChannelId ?? null,
    movedToSequence: row.movedToSequence ?? null,
    movedToAccessible: !!row.movedToAccessible,
    movedFromMessageId: row.movedFromMessageId ?? null,
    movedFromChannelId: row.movedFromChannelId ?? null,
    movedFromSequence: row.movedFromSequence ?? null,
  };
}
