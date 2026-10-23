import { Component, input, model } from '@angular/core';

export interface TabItem {
  id: string;
  label: string;
}

@Component({
  selector: 'vc-tabs',
  standalone: true,
  template: `
    <div class="vc-tabs" role="tablist">
      @for (tab of tabs(); track tab.id; let index = $index) {
        <button
          type="button"
          class="vc-tabs__tab"
          role="tab"
          [id]="tabId(tab.id)"
          [attr.aria-selected]="isActive(tab.id)"
          [attr.tabindex]="isActive(tab.id) ? 0 : -1"
          (click)="active.set(tab.id)"
          (keydown)="onKey($event, index)"
        >
          {{ tab.label }}
        </button>
      }
    </div>
  `,
  styles: `:host { display: block; min-width: 0; }`,
})
export class Tabs {
  readonly tabs = input<TabItem[]>([]);
  readonly active = model('');

  isActive(id: string): boolean {
    return (this.active() || this.tabs()[0]?.id) === id;
  }

  tabId(id: string): string {
    return `vc-tab-${id}`;
  }

  onKey(event: KeyboardEvent, index: number): void {
    const items = this.tabs();
    if (!items.length) return;
    if (event.key !== 'ArrowRight' && event.key !== 'ArrowLeft' && event.key !== 'Home' && event.key !== 'End') {
      return;
    }
    event.preventDefault();
    const next =
      event.key === 'Home'
        ? 0
        : event.key === 'End'
          ? items.length - 1
          : event.key === 'ArrowRight'
            ? (index + 1) % items.length
            : (index - 1 + items.length) % items.length;
    this.active.set(items[next].id);
  }
}
