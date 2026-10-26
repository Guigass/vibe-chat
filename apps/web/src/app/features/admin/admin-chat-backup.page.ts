import { Component, inject, OnInit, signal } from '@angular/core';
import { ChatBackupApiService, ChatBackupList } from '../../core/api/chat-backup-api.service';
import { ui } from '../../core/i18n/strings';
import { AdminContextService } from './admin-context.service';
import { AdminAreaId } from './admin-permissions';

@Component({
  selector: 'vc-admin-chat-backup',
  standalone: true,
  templateUrl: './admin-chat-backup.page.html',
  styleUrl: './admin-shared.scss',
})
export class AdminChatBackupPage implements OnInit {
  readonly areaId: AdminAreaId = 'chatBackup';
  readonly ui = ui;
  readonly kinds = ['sftp', 'ftp', 'smb', 's3', 'webdav', 'drive'];

  private readonly api = inject(ChatBackupApiService);
  readonly ctx = inject(AdminContextService);

  readonly loading = signal(true);
  readonly disabled = signal(false);
  readonly denied = signal(false);
  readonly loadError = signal(false);
  readonly notice = signal('');
  readonly plan = signal('');
  readonly model = signal<ChatBackupList | null>(null);
  readonly name = signal('');
  readonly kind = signal('sftp');
  readonly host = signal('');
  readonly secret = signal('');
  readonly jobName = signal('');
  readonly destinationId = signal('');
  readonly includeFiles = signal(false);
  readonly file = signal<File | null>(null);

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  asValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  asChecked(event: Event): boolean {
    return (event.target as HTMLInputElement).checked;
  }

  async createDestination(): Promise<void> {
    await this.run(async (workspaceId) => {
      const created = await this.api.createDestination(workspaceId, crypto.randomUUID(), {
        name: this.name(),
        kind: this.kind(),
        host: this.host(),
        port: 0,
        remotePath: 'chat-backup',
        username: this.kind() === 'drive' ? '' : 'backup',
        bucket: this.kind() === 's3' ? 'vibechat' : '',
        useSsl: this.kind() === 'webdav' || this.kind() === 'drive',
        secret: this.secret(),
      });
      this.secret.set('');
      this.destinationId.set(created.id);
    });
  }

  async test(destinationId: string): Promise<void> {
    await this.run((workspaceId) => this.api.testDestination(workspaceId, destinationId));
  }

  async createJob(): Promise<void> {
    const destinationId = this.destinationId();
    if (!destinationId) {
      return;
    }

    await this.run((workspaceId) =>
      this.api.createJob(workspaceId, crypto.randomUUID(), destinationId, this.jobName(), this.includeFiles()));
  }

  async runJob(jobId: string): Promise<void> {
    await this.run((workspaceId) => this.api.run(workspaceId, jobId, crypto.randomUUID()));
  }

  async setPaused(jobId: string, paused: boolean): Promise<void> {
    await this.run((workspaceId) => this.api.setPaused(workspaceId, jobId, paused));
  }

  async download(includeAttachments: boolean): Promise<void> {
    await this.run((workspaceId) => this.api.download(workspaceId, includeAttachments));
  }

  onFile(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.file.set(input.files?.[0] ?? null);
    this.plan.set('');
  }

  async validate(): Promise<void> {
    const file = this.file();
    if (!file) {
      return;
    }

    await this.run(async (workspaceId) => {
      const result = await this.api.validateImport(workspaceId, file);
      this.plan.set(`${result.insert}/${result.skip}`);
    });
  }

  async execute(): Promise<void> {
    const file = this.file();
    if (!file || !this.plan()) {
      return;
    }

    await this.run(async (workspaceId) => {
      await this.api.executeImport(workspaceId, crypto.randomUUID(), file);
      this.plan.set('');
      this.file.set(null);
    });
  }

  private async run(action: (workspaceId: string) => Promise<unknown>): Promise<void> {
    const workspaceId = this.ctx.workspace()?.id;
    if (!workspaceId) {
      return;
    }

    this.notice.set('');
    this.loadError.set(false);
    try {
      await action(workspaceId);
      this.notice.set(ui.chatBackupSaved);
      await this.load();
    } catch {
      this.loadError.set(true);
    }
  }

  private async load(): Promise<void> {
    await this.ctx.ensureReady();
    const workspaceId = this.ctx.workspace()?.id;
    if (!workspaceId) {
      this.loading.set(false);
      this.denied.set(true);
      return;
    }

    this.loading.set(true);
    try {
      this.model.set(await this.api.list(workspaceId));
      const first = this.model()?.destinations[0]?.id;
      if (!this.destinationId() && first) {
        this.destinationId.set(first);
      }
    } catch (error) {
      const status = (error as { status?: number }).status;
      const message = error instanceof Error ? error.message : '';
      if (status === 404 && message.includes('ChatBackupDisabled')) {
        this.disabled.set(true);
      } else if (status === 403) {
        this.denied.set(true);
      } else {
        this.loadError.set(true);
      }
    } finally {
      this.loading.set(false);
    }
  }
}
