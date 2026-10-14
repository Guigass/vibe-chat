import { Component, inject, signal } from '@angular/core';
import { ui } from '../../../core/i18n/strings';
import { KnowledgeStore } from '../../../core/services/knowledge.store';

@Component({
  selector: 'vc-knowledge-panel',
  standalone: true,
  template: `
    <section class="knowledge-panel" [attr.aria-label]="ui.knowledgePanel" data-testid="knowledge-panel">
      <header>
        <h2>{{ ui.knowledgePanel }}</h2>
        <button type="button" (click)="knowledge.closePanel()">{{ ui.knowledgeClose }}</button>
      </header>
      @if (knowledge.error(); as error) {
        <p role="alert">{{ error }}</p>
      }
      @if (knowledge.conflict()) {
        <p role="alert" data-testid="knowledge-conflict">{{ ui.knowledgeConflict }}</p>
        <button type="button" (click)="knowledge.reloadPage()">{{ ui.knowledgeReload }}</button>
      }
      <label>
        <span class="vc-sr">{{ ui.knowledgeSearch }}</span>
        <input
          type="search"
          [placeholder]="ui.knowledgeSearch"
          [attr.aria-label]="ui.knowledgeSearch"
          data-testid="knowledge-search"
          (input)="onSearch($event)"
        />
      </label>
      @if (knowledge.canEdit()) {
        <form (submit)="createCollection($event)">
          <label>
            {{ ui.knowledgeCollectionTitle }}
            <input data-testid="knowledge-collection-title" [value]="collectionTitle()" (input)="collectionTitle.set(inputValue($event))" required />
          </label>
          <label>
            {{ ui.knowledgeScopeWorkspace }}
            <select data-testid="knowledge-scope" [value]="scope()" (change)="scope.set(selectValue($event))">
              <option value="workspace">{{ ui.knowledgeScopeWorkspace }}</option>
              <option value="channel">{{ ui.knowledgeScopeChannel }}</option>
            </select>
          </label>
          <button type="submit" data-testid="knowledge-create-collection" [disabled]="knowledge.saving()">{{ ui.knowledgeCreateCollection }}</button>
        </form>
      }
      <ul>
        @for (collection of knowledge.collections(); track collection.id) {
          <li [style.padding-inline-start]="collection.parentId ? 'var(--vc-space-3)' : '0'">
            <button type="button" (click)="knowledge.selectCollection(collection.id)">{{ collection.title }}</button>
          </li>
        }
      </ul>
      @if (knowledge.results(); as results) {
        <ul>
          @for (item of results; track item.id) {
            <li><button type="button" (click)="knowledge.selectPage(item.id)">{{ item.title }}</button></li>
          } @empty {
            <li>{{ ui.knowledgeEmpty }}</li>
          }
        </ul>
      }
      @if (knowledge.selectedCollectionId()) {
        @if (knowledge.canEdit()) {
          <form (submit)="createPage($event)">
            <label>
              {{ ui.knowledgeTitleLabel }}
              <input data-testid="knowledge-page-title" [value]="pageTitle()" (input)="pageTitle.set(inputValue($event))" required />
            </label>
            <button type="submit" data-testid="knowledge-create-page" [disabled]="knowledge.saving()">{{ ui.knowledgeCreatePage }}</button>
          </form>
        }
        <ul>
          @for (item of knowledge.pages(); track item.id) {
            <li><button type="button" (click)="knowledge.selectPage(item.id)">{{ item.title }}</button></li>
          } @empty {
            <li>{{ ui.knowledgeEmpty }}</li>
          }
        </ul>
      }
      @if (knowledge.page(); as page) {
        <form (submit)="save($event)">
          @if (knowledge.draftLocal()) {
            <p>{{ ui.knowledgeDraft }}</p>
          }
          <label>
            {{ ui.knowledgeTitleLabel }}
            <input data-testid="knowledge-edit-title" [value]="knowledge.title()" (input)="knowledge.setTitle(inputValue($event))" />
          </label>
          <label>
            {{ ui.knowledgeBodyLabel }}
            <textarea data-testid="knowledge-body" rows="8" [value]="knowledge.body()" (input)="knowledge.setBody(inputValue($event))"></textarea>
          </label>
          <h3>{{ ui.knowledgePreview }}</h3>
          <pre data-testid="knowledge-preview">{{ knowledge.body() }}</pre>
          @if (knowledge.canEdit()) {
            <button type="submit" data-testid="knowledge-save" [disabled]="knowledge.saving()">{{ ui.knowledgeSave }}</button>
          }
        </form>
        <ul>
          @for (ref of page.references; track ref.kind + ref.targetId) {
            <li>{{ ref.state === 'visible' ? (ref.label || ref.kind) : ui.knowledgeRedacted }}</li>
          }
        </ul>
        <h3>{{ ui.knowledgeHistory }}</h3>
        <ul>
          @for (version of knowledge.history(); track version.version) {
            <li>
              <span>v{{ version.version }}</span>
              @if (knowledge.canManage() && version.version !== page.version) {
                <button type="button" (click)="knowledge.restore(version.version)">{{ ui.knowledgeRestore }}</button>
              }
            </li>
          }
        </ul>
        @if (knowledge.canManage()) {
          <button type="button" data-testid="knowledge-delete" (click)="knowledge.removePage()">{{ ui.knowledgeDelete }}</button>
        }
      }
    </section>
  `,
  styles: `
    .knowledge-panel {
      display: grid;
      gap: var(--vc-space-2);
      padding: var(--vc-space-3);
      min-width: 0;
    }
    .knowledge-panel header { display: flex; justify-content: space-between; align-items: center; gap: var(--vc-space-2); }
    .knowledge-panel h2, .knowledge-panel h3, .knowledge-panel p { margin: 0; }
    .knowledge-panel h2 { font-size: var(--vc-text-sm); }
    .knowledge-panel form, .knowledge-panel label { display: grid; gap: var(--vc-space-1); }
    .knowledge-panel input, .knowledge-panel textarea, .knowledge-panel select, .knowledge-panel button {
      font: inherit;
      color: var(--vc-ink);
      background: transparent;
      border: 1px solid var(--vc-border);
      border-radius: var(--vc-radius-md);
      padding: 0.35rem 0.5rem;
    }
    .knowledge-panel button { cursor: pointer; text-align: left; }
    .knowledge-panel button:focus-visible, .knowledge-panel input:focus-visible, .knowledge-panel textarea:focus-visible {
      outline: 2px solid var(--vc-brand);
      outline-offset: 2px;
    }
    .knowledge-panel ul { list-style: none; margin: 0; padding: 0; display: grid; gap: var(--vc-space-1); }
    .knowledge-panel pre {
      margin: 0;
      white-space: pre-wrap;
      font: inherit;
      color: var(--vc-ink-muted);
    }
    .vc-sr {
      position: absolute;
      width: 1px;
      height: 1px;
      padding: 0;
      margin: -1px;
      overflow: hidden;
      clip: rect(0, 0, 0, 0);
      border: 0;
    }
  `,
})
export class KnowledgePanel {
  readonly ui = ui;
  readonly knowledge = inject(KnowledgeStore);
  readonly collectionTitle = signal('');
  readonly pageTitle = signal('');
  readonly scope = signal<'workspace' | 'channel'>('workspace');

  inputValue(event: Event): string {
    return (event.target as HTMLInputElement | HTMLTextAreaElement).value;
  }

  selectValue(event: Event): 'workspace' | 'channel' {
    return (event.target as HTMLSelectElement).value === 'channel' ? 'channel' : 'workspace';
  }

  onSearch(event: Event): void {
    void this.knowledge.search(this.inputValue(event));
  }

  createCollection(event: Event): void {
    event.preventDefault();
    const title = this.collectionTitle().trim();
    if (!title) return;
    void this.knowledge.createCollection(title, this.scope()).then(() => this.collectionTitle.set(''));
  }

  createPage(event: Event): void {
    event.preventDefault();
    const title = this.pageTitle().trim();
    if (!title) return;
    void this.knowledge.createPage(title).then(() => this.pageTitle.set(''));
  }

  save(event: Event): void {
    event.preventDefault();
    void this.knowledge.save();
  }
}
