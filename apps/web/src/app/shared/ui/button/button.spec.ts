import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { Button } from './button';

describe('Button (UX-001)', () => {
  it('declares a base ink color so variants are not the only color source', async () => {
    await TestBed.configureTestingModule({
      imports: [Button],
    }).compileComponents();

    const fixture = TestBed.createComponent(Button);
    fixture.componentRef.setInput('variant', 'ghost');
    fixture.detectChanges();

    const cmp = Button as unknown as { ɵcmp: { styles: string[] } };
    const css = cmp.ɵcmp.styles.join('\n');
    // Emulated encapsulation rewrites selectors to `.vc-btn[_ngcontent-%COMP%]`.
    expect(css).toContain('color: var(--vc-ink)');
    expect(css).toMatch(/\.vc-btn(?:\[[^\]]+\])?\s*\{[^}]*color:\s*var\(--vc-ink\)/s);
  });
});
