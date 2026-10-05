import { Component, effect, inject } from '@angular/core';
import { ui } from '../../../core/i18n/strings';
import { drawAudioWaveform } from '../../../shared/utils/audio';
import { IconButton } from '../../../shared/ui';
import { formatDuration } from './audio-recorder';
import { AudioRecorderService } from './audio-recorder.service';
import { AttachmentQueueService } from './attachment-queue.service';
import { MessageStore } from '../../../core/services/message.store';
import { ComposerState } from './composer-state';

@Component({
  selector: 'vc-composer-audio-controls',
  standalone: true,
  imports: [IconButton],
  templateUrl: './composer-audio-controls.html',
  styleUrl: './composer-audio-controls.scss',
})
export class ComposerAudioControls {
  readonly audioRecorder = inject(AudioRecorderService);
  readonly attachments = inject(AttachmentQueueService);
  readonly messages = inject(MessageStore);
  private readonly state = inject(ComposerState);
  readonly ui = ui;
  readonly formatDuration = formatDuration;

  constructor() {
    effect(() => {
      if (this.audioRecorder.phase() !== 'recording') return;
      const canvas = document.querySelector('.composer__audio-panel canvas');
      drawAudioWaveform(canvas as HTMLCanvasElement | null, this.audioRecorder.liveWaveform());
    });
  }

  async startRecording(): Promise<void> {
    const error = await this.audioRecorder.start();
    this.state.validationError.set(error);
  }

  stopRecording(): void {
    void this.audioRecorder.stop();
  }

  discardRecording(): void {
    this.audioRecorder.discard();
  }
}
