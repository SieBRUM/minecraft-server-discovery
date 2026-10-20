import { flag, timeAgo, LoginLabelPipe } from './pipes';

describe('pipes', () => {
  const now = Date.parse('2026-01-01T12:00:00Z');

  it('formats relative times', () => {
    expect(timeAgo('2026-01-01T11:00:00Z', now)).toBe('1 hour ago');
    expect(timeAgo('2025-12-29T12:00:00Z', now)).toBe('3 days ago');
    expect(timeAgo('2026-01-01T12:00:00Z', now)).toBe('now');
    expect(timeAgo(null, now)).toBe('');
    expect(timeAgo('not a date', now)).toBe('');
  });

  it('turns country codes into flags and rejects anything else', () => {
    expect(flag('NL')).toBe('🇳🇱');
    expect(flag('us')).toBe('🇺🇸');
    expect(flag('NLD')).toBe('');
    expect(flag('<b')).toBe('');
    expect(flag(null)).toBe('');
  });

  it('labels login probe results', () => {
    const pipe = new LoginLabelPipe();
    expect(pipe.transform('OfflineMode')).toBe('Offline mode (cracked)');
    expect(pipe.transform('Unknown')).toBe('Unknown');
  });
});
