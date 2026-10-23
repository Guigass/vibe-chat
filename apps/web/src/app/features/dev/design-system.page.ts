import { Component, signal } from '@angular/core';
import { ui } from '../../core/i18n/strings';
import {
  Badge,
  Button,
  Card,
  Cluster,
  DataTable,
  EmptyState,
  Input,
  Modal,
  PageShell,
  Select,
  Split,
  Stack,
  Tabs,
  Toolbar,
  UiList,
} from '../../shared/ui';

@Component({
  selector: 'vc-design-system-page',
  standalone: true,
  imports: [
    Badge,
    Button,
    Card,
    Cluster,
    DataTable,
    EmptyState,
    Input,
    Modal,
    PageShell,
    Select,
    Split,
    Stack,
    Tabs,
    Toolbar,
    UiList,
  ],
  templateUrl: './design-system.page.html',
  styleUrl: './design-system.page.scss',
})
export class DesignSystemPage {
  readonly ui = ui;
  readonly modalOpen = signal(false);
  readonly tab = signal('general');
  readonly channel = signal('geral');
  readonly name = signal('');
  readonly channelOptions = [
    { value: 'geral', label: ui.dsOptionGeneral },
    { value: 'random', label: ui.dsOptionRandom },
  ];

  readonly tabs = [
    { id: 'general', label: ui.dsTabGeneral },
    { id: 'compact', label: ui.dsTabCompact },
  ];

  readonly swatches = [
    { name: '--vc-brand', color: 'var(--vc-brand)' },
    { name: '--vc-brand-soft', color: 'var(--vc-brand-soft)' },
    { name: '--vc-ink', color: 'var(--vc-ink)' },
    { name: '--vc-surface', color: 'var(--vc-surface)' },
    { name: '--vc-surface-elevated', color: 'var(--vc-surface-elevated)' },
    { name: '--vc-danger', color: 'var(--vc-danger)' },
    { name: '--vc-success', color: 'var(--vc-success)' },
    { name: '--vc-warning', color: 'var(--vc-warning)' },
  ];
}
