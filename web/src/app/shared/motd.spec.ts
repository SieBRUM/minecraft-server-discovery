import { TestBed } from '@angular/core/testing';
import { MotdSegment } from '../core/models';
import { Motd } from './motd';

function segment(text: string, extra: Partial<MotdSegment> = {}): MotdSegment {
  return {
    text,
    color: null,
    bold: false,
    italic: false,
    underlined: false,
    strikethrough: false,
    obfuscated: false,
    ...extra,
  };
}

async function render(segments: MotdSegment[], fallback = ''): Promise<HTMLElement> {
  const fixture = TestBed.createComponent(Motd);
  fixture.componentRef.setInput('segments', segments);
  fixture.componentRef.setInput('fallback', fallback);
  await fixture.whenStable();
  return fixture.nativeElement as HTMLElement;
}

describe('Motd', () => {
  it('renders segments with styles', async () => {
    const el = await render([segment('Gold', { color: '#ffaa00', bold: true }), segment(' plain')]);
    const spans = el.querySelectorAll('.motd > span');
    expect(spans.length).toBe(2);
    expect(spans[0].textContent).toBe('Gold');
    expect(spans[0].classList.contains('b')).toBe(true);
    expect((spans[0] as HTMLElement).style.color).toBe('rgb(255, 170, 0)');
  });

  it('never interprets server text as HTML', async () => {
    const el = await render([segment('<img src=x onerror="alert(1)"><script>alert(2)</script>')]);
    expect(el.querySelector('img')).toBeNull();
    expect(el.querySelector('script')).toBeNull();
    expect(el.textContent).toContain('<img src=x onerror="alert(1)">');
  });

  it('does not apply unsafe colour values', async () => {
    const el = await render([segment('x', { color: 'url(javascript:alert(1))' })]);
    const span = el.querySelector('.motd > span') as HTMLElement;
    expect(span.style.color).toBe('');
    expect(span.getAttribute('style') ?? '').not.toContain('javascript');
  });

  it('shows the fallback when there are no segments', async () => {
    const el = await render([], 'plain text');
    expect(el.textContent?.trim()).toBe('plain text');
  });
});
