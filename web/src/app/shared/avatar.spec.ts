import { pixelAvatar } from './avatar';

describe('pixelAvatar', () => {
  it('is deterministic and differs per name', () => {
    expect(pixelAvatar('Notch')).toBe(pixelAvatar('Notch'));
    expect(pixelAvatar('Notch')).not.toBe(pixelAvatar('jeb_'));
  });

  it('never embeds the name in the image', () => {
    const svg = atob(pixelAvatar('<script>alert(1)</script>').split(',')[1]);
    expect(svg).not.toContain('script');
    expect(svg.startsWith('<svg')).toBe(true);
  });
});
