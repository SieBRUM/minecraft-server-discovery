import { Pipe, PipeTransform } from '@angular/core';

const UNITS: [Intl.RelativeTimeFormatUnit, number][] = [
  ['year', 365 * 24 * 3600],
  ['month', 30 * 24 * 3600],
  ['day', 24 * 3600],
  ['hour', 3600],
  ['minute', 60],
  ['second', 1],
];

const relative = new Intl.RelativeTimeFormat('en', { numeric: 'auto' });

/** "3 hours ago" for an ISO timestamp. */
export function timeAgo(iso: string | null | undefined, now: number = Date.now()): string {
  if (!iso) {
    return '';
  }
  const seconds = Math.round((new Date(iso).getTime() - now) / 1000);
  if (!Number.isFinite(seconds)) {
    return '';
  }
  for (const [unit, size] of UNITS) {
    if (Math.abs(seconds) >= size || unit === 'second') {
      return relative.format(Math.round(seconds / size), unit);
    }
  }
  return '';
}

@Pipe({ name: 'timeAgo' })
export class TimeAgoPipe implements PipeTransform {
  transform(iso: string | null | undefined): string {
    return timeAgo(iso);
  }
}

/** Turns an ISO 3166 alpha-2 code into its flag emoji ("NL" -> 🇳🇱). Anything else gives ''. */
export function flag(code: string | null | undefined): string {
  if (!code || !/^[A-Za-z]{2}$/.test(code)) {
    return '';
  }
  const base = 0x1f1e6 - 'A'.charCodeAt(0);
  const upper = code.toUpperCase();
  return String.fromCodePoint(base + upper.charCodeAt(0), base + upper.charCodeAt(1));
}

@Pipe({ name: 'flag' })
export class FlagPipe implements PipeTransform {
  transform(code: string | null | undefined): string {
    return flag(code);
  }
}

const LOGIN_LABELS: Record<string, string> = {
  NotProbed: 'Not checked',
  OnlineMode: 'Online mode (premium only)',
  OfflineMode: 'Offline mode (cracked)',
  Whitelisted: 'Whitelist',
  Kicked: 'Kicked the probe',
  PluginRequest: 'Proxy / modded handshake',
  Failed: 'Probe failed',
};

@Pipe({ name: 'loginLabel' })
export class LoginLabelPipe implements PipeTransform {
  transform(value: string | null | undefined): string {
    return value ? (LOGIN_LABELS[value] ?? value) : '';
  }
}
