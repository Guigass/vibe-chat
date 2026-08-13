/** @vitest-environment jsdom */
import '@angular/compiler';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { Channel, SpaceGroup, WorkspaceMember } from '../../models/chat.models';
import { SidebarNav } from './sidebar-nav';

const groups: SpaceGroup[] = [
  {
    space: { id: 'sp-1', name: 'Geral', workspaceId: 'ws-1', order: 0 },
    channels: [
      { id: 'ch-1', name: 'roadmap', workspaceId: 'ws-1', unreadCount: 0, isDirect: false, isPrivate: false },
      { id: 'ch-2', name: 'engenharia', workspaceId: 'ws-1', unreadCount: 2, isDirect: false, isPrivate: false },
    ],
  },
];

const directs: Channel[] = [
  { id: 'dm-1', name: 'Alice', workspaceId: 'ws-1', unreadCount: 0, isDirect: true, peerUserId: 'u-1' },
];

const members: WorkspaceMember[] = [
  { userId: 'u-2', displayName: 'Bob', role: 'Member', email: 'bob@example.com' },
];

describe('SidebarNav (B-184)', () => {
  let fixture: ReturnType<typeof TestBed.createComponent<SidebarNav>>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SidebarNav],
    }).compileComponents();

    fixture = TestBed.createComponent(SidebarNav);
    fixture.componentRef.setInput('groups', groups);
    fixture.componentRef.setInput('directs', directs);
    fixture.componentRef.setInput('members', members);
    fixture.detectChanges();
  });

  it('filters channels, DMs and members client-side', () => {
    fixture.componentInstance.filterQuery.set('bob');
    fixture.detectChanges();

    expect(fixture.componentInstance.filteredGroups().length).toBe(0);
    expect(fixture.componentInstance.filteredDirects().length).toBe(0);
    expect(fixture.componentInstance.filteredMembers().length).toBe(1);
    expect(fixture.nativeElement.textContent).toContain('Bob');
  });

  it('shows empty state when filter has no matches', () => {
    fixture.componentInstance.filterQuery.set('zzz');
    fixture.detectChanges();

    expect(fixture.componentInstance.isEmpty()).toBe(true);
    expect(fixture.nativeElement.querySelector('.vc-sidebar-nav__empty')).toBeTruthy();
  });

  it('clears filter on Escape while the filter input is focused', () => {
    fixture.componentInstance.filterQuery.set('eng');
    fixture.detectChanges();

    const input = document.getElementById('vc-nav-filter') as HTMLInputElement;
    input.focus();
    const event = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true });
    Object.defineProperty(event, 'target', { value: input });

    fixture.componentInstance.onGlobalKeydown(event);
    fixture.detectChanges();

    expect(fixture.componentInstance.filterQuery()).toBe('');
  });

  it('renders compact icon rail without section labels', () => {
    fixture.componentRef.setInput('compact', true);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.vc-sidebar-nav--compact')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('.vc-sidebar-nav__label')).toBeNull();
    expect(fixture.nativeElement.querySelector('.vc-channel--compact')).toBeTruthy();
  });

  it('hides the filter in compact mode', () => {
    fixture.componentInstance.filterQuery.set('eng');
    fixture.componentRef.setInput('compact', true);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('#vc-nav-filter')).toBeNull();
    expect(fixture.nativeElement.querySelector('.vc-sidebar-nav__filter')).toBeNull();
    expect(fixture.nativeElement.querySelector('[aria-label="Filtrar canais, recentes e membros"]')).toBeNull();
    expect(fixture.componentInstance.filterQuery()).toBe('');
  });

  it('keeps the filter in expanded mode', () => {
    expect(fixture.nativeElement.querySelector('#vc-nav-filter')).toBeTruthy();
  });

  it('uses overlay tooltip attrs instead of native title in compact mode', () => {
    fixture.componentRef.setInput('compact', true);
    fixture.detectChanges();

    const channelBtn = fixture.nativeElement.querySelector('.vc-channel--compact') as HTMLButtonElement;
    expect(channelBtn.getAttribute('title')).toBeNull();
    expect(channelBtn.getAttribute('aria-label')).toContain('#roadmap');

    const memberBtn = fixture.nativeElement.querySelector('.vc-sidebar-nav__member--compact') as HTMLButtonElement;
    expect(memberBtn.getAttribute('title')).toBeNull();
    expect(memberBtn.getAttribute('aria-label')).toContain('Bob');
  });

  it('labels the DM block as Recentes', () => {
    expect(fixture.nativeElement.querySelector('[aria-label="Recentes"]')).toBeTruthy();
    expect(fixture.nativeElement.textContent).toContain('Recentes');
    expect(fixture.nativeElement.textContent).not.toContain('Mensagens diretas');
  });

  it('hides new-channel control in compact mode', () => {
    fixture.componentRef.setInput('canCreate', true);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Novo channel');

    fixture.componentRef.setInput('compact', true);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).not.toContain('Novo channel');
    expect(fixture.nativeElement.querySelector('[aria-label="Novo channel"]')).toBeNull();
  });
});
