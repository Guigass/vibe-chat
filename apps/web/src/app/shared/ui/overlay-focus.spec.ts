import { TestBed } from '@angular/core/testing';
import { CdkTrapFocus, InteractivityChecker } from '@angular/cdk/a11y';
import { By } from '@angular/platform-browser';
import { describe, expect, it, vi } from 'vitest';
import { ChannelStore } from '../../core/services/channel.store';
import { ImageLightbox } from './image-lightbox/image-lightbox';
import { EmojiPicker } from './emoji-picker/emoji-picker';
import { ForwardDialog } from '../../features/chat/forward-dialog/forward-dialog';

describe('B-103 overlay focus lifecycle', () => {
  for (const component of [ImageLightbox, EmojiPicker, ForwardDialog]) {
    it(`${component.name} captures focus, wraps it and restores the trigger`, async () => {
      vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({ categories: [], keywords: {} })));
      await TestBed.configureTestingModule({
        imports: [component],
        providers: [{ provide: ChannelStore, useValue: { channels: () => [], publicChannels: () => [], directChannels: () => [] } }],
      }).compileComponents();
      // jsdom has no layout; preserve disabled/tabindex semantics when checking visibility.
      const checker = TestBed.inject(InteractivityChecker);
      vi.spyOn(checker, 'isVisible').mockReturnValue(true);
      const trigger = document.createElement('button');
      document.body.append(trigger);
      trigger.focus();
      const fixture = TestBed.createComponent(component as typeof ImageLightbox);
      if (component === ImageLightbox) {
        fixture.componentRef.setInput('images', [{ id: 'image', url: '/test.png', alt: 'test' }]);
      }
      fixture.componentRef.setInput('open', true);
      fixture.detectChanges();
      await fixture.whenStable();
      const trapElement = fixture.debugElement.query(By.directive(CdkTrapFocus));
      expect(trapElement).toBeTruthy();
      const trap = trapElement.injector.get(CdkTrapFocus).focusTrap;
      expect(trap.enabled).toBe(true);
      expect(trapElement.nativeElement.contains(document.activeElement)).toBe(true);
      expect(trap.focusLastTabbableElement()).toBe(true);
      expect(trapElement.nativeElement.contains(document.activeElement)).toBe(true);
      expect(trap.focusFirstTabbableElement()).toBe(true);
      fixture.componentRef.setInput('open', false);
      fixture.detectChanges();
      await fixture.whenStable();
      expect(document.activeElement).toBe(trigger);
      fixture.destroy();
      trigger.remove();
    });
  }
});
