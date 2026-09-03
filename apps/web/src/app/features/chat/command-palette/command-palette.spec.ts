/** @vitest-environment jsdom */
import '@angular/compiler';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthService } from '../../../core/auth/auth.service';
import { ChannelStore } from '../../../core/services/channel.store';
import { CommandPaletteService } from '../../../core/services/command-palette.service';
import { MessageStore } from '../../../core/services/message.store';
import { SavedStore } from '../../../core/services/saved.store';
import { ThemeService } from '../../../core/services/theme.service';
import { SlashCommandsService } from '../composer/slash-commands.service';
import { CommandPalette } from './command-palette';

describe('CommandPalette (OPS-E2E-B099)', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CommandPalette],
      providers: [
        provideRouter([]),
        CommandPaletteService,
        {
          provide: AuthService,
          useValue: { profile: () => ({ id: 'u-alice', name: 'Alice' }) },
        },
        {
          provide: ChannelStore,
          useValue: {
            publicChannels: () => [{ id: 'ch-geral', name: 'geral' }],
            peerCandidates: () => [],
            workspaces: () => [{ id: 'ws-1', name: 'Acme', role: 'Member' }],
            activeWorkspace: () => ({ id: 'ws-1' }),
            selectChannel: vi.fn(),
            openDirectMessage: vi.fn(),
            prefillComposer: vi.fn(),
          },
        },
        {
          provide: MessageStore,
          useValue: { loadChannel: vi.fn(), markActiveChannelRead: vi.fn() },
        },
        {
          provide: SavedStore,
          useValue: { openPanel: vi.fn() },
        },
        {
          provide: ThemeService,
          useValue: {
            theme: signal('light'),
            density: signal('comfortable'),
            toggleTheme: vi.fn(),
            toggleDensity: vi.fn(),
          },
        },
        {
          provide: SlashCommandsService,
          useValue: {
            listCommands: vi.fn().mockResolvedValue([]),
            execute: vi.fn(),
          },
        },
      ],
    }).compileComponents();
  });

  it('focuses the query input when the palette opens', async () => {
    const fixture = TestBed.createComponent(CommandPalette);
    fixture.detectChanges();

    const palette = TestBed.inject(CommandPaletteService);
    palette.openPalette();
    fixture.detectChanges();
    await fixture.whenStable();
    await Promise.resolve();
    fixture.detectChanges();
    await Promise.resolve();

    const query = (fixture.nativeElement as HTMLElement).querySelector(
      '[data-testid="command-palette-query"]',
    ) as HTMLInputElement | null;
    expect(query).toBeTruthy();
    expect(document.activeElement).toBe(query);
  });
});
